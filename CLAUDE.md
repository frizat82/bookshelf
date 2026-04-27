# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this project is

**Bookshelf** is a fork of [Readarr](https://github.com/Readarr/Readarr) — an ebook/audiobook collection manager. Key differences from upstream:

- **Hardcover** metadata provider (higher quality than Goodreads; not backward-compatible with existing Readarr DBs)
- **Goodreads** metadata still supported via `softcover` Docker tags
- **slskd (Soulseek)** download client added (`src/NzbDrone.Core/Download/Clients/Soulseek/`)
- **Calibre-Web Automated (CWA)** integration: downloads are handed off to CWA for import into the Calibre library
- Published to `ghcr.io/frizat82/bookshelf` — the `hardcover` tag is the floating latest

The main branch is `main`. Docker images are pushed on every push to `main` via CI.

## Runtime requirements

.NET 9 is required. The local machine may only have .NET 8. Install .NET 9 via:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0 --install-dir ~/.dotnet
export PATH="$HOME/.dotnet:$PATH"
```

`mise.toml` at the repo root pins `dotnet = "9.0.300"` and `node = "20.19.4"`. If `mise` is available, `mise exec -- <command>` will use the right versions.

## Build commands

```bash
# Full build (backend + frontend) — what CI runs
./build.sh --backend --frontend

# Backend only (faster for C# changes)
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Readarr.sln --no-restore

# Restore packages first if needed
dotnet restore src/Readarr.sln
```

## Test commands

```bash
# Run all unit tests (after building)
./test.sh Linux Unit Test

# Run a single test project
export PATH="$HOME/.dotnet:$PATH"
dotnet test src/NzbDrone.Core.Test/ --filter "ClassName~SlskdFixture"

# Run a single test by name
dotnet test src/NzbDrone.Core.Test/ --filter "FullyQualifiedName~SlskdFixture.should_return_completed"
```

## Before pushing changes

Always build and verify locally before committing, especially for C# changes:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Readarr.sln --no-restore 2>&1 | tail -20
```

CI treats StyleCop violations and NuGet CVE warnings as hard errors. A local `0 Error(s)` is the required check before pushing.

## StyleCop rules (enforced as errors in CI)

Violations cause build failures. The most common ones to watch:

- **SA1214**: `readonly` fields must come before non-readonly fields in a class
- **SA1515**: a single-line comment must be preceded by a blank line
- **SA1512**: a single-line comment must not be followed by a blank line

## Package management

The project uses **Central Package Management**. Package versions go in `src/Directory.Packages.props` as `<PackageVersion>` entries — never add `Version=` directly to a `<PackageReference>` in a `.csproj`.

NuGet vulnerability warnings are treated as errors (`NU1902`). When bumping a package to fix a CVE, update `Directory.Packages.props`.

## Architecture overview

### Backend (`src/NzbDrone.Core/`)

Standard Readarr layered architecture:

- **Commands** (`Messaging/Commands/`) — units of work pushed onto the command queue via `IManageCommandQueue.Push()`. Executed by `CommandExecutor`. Key commands: `RescanFoldersCommand`, `BulkRefreshAuthorCommand`, `RefreshAuthorCommand`.
- **Events** (`Messaging/Events/`) — published after state changes via `IEventAggregator.PublishEvent()`. Handlers implement `IHandle<TEvent>`.
- **DI** — DryIoc container; services are auto-discovered by implementing marker interfaces (`IExecute<TCommand>`, `IHandle<TEvent>`, `IProvide*`, etc.).

### Scan pipeline

1. `RootFolderWatchingService` — `FileSystemWatcher` on each root folder. On change events, debounces 30s then calls `ScanPending()`, which scopes the rescan to the immediate **author subfolder** (e.g. `/books/Author Name/`), not the root. Pushes `RescanFoldersCommand`.
2. `DiskScanService.Execute(RescanFoldersCommand)` → `Scan()` — collects book files, runs `ImportDecisionMaker`, then `ImportApprovedBooks`.
3. `ImportApprovedBooks` — after importing new authors, pushes `BulkRefreshAuthorCommand`.
4. `RefreshAuthorService.Rescan()` — pushes a follow-up `RescanFoldersCommand` scoped to the refreshed author paths (not all root folders).

**Critical invariant**: nothing in this pipeline should push a `RescanFoldersCommand` with `Folders = null` or `Folders = [rootFolder]` in response to a single file change — that triggers a full 7000+ book library scan and causes an endless rescan loop.

**Calibre library FSW behaviour**: When the books folder is also managed by Calibre/CWA, Calibre writes `metadata.opf` and `cover.jpg` files and occasionally rewrites ebook files during library operations. This generates FSW events and can cause `MissingFromDisk` churn if Readarr scans mid-operation.

### Scan loop diagnosis

**Step 1 — check what's actually scanning:**
```bash
grep -E "Scanning|Identifying|ScanPending|Skipping root|RescanFolders" /mnt/docker/bookshelf/logs/readarr.txt | tail -50
```
Key things to check: (1) is "Scanning /root/folder/" (full root) appearing, or "Scanning /root/folder/Author Name/" (author-scoped)? (2) what is the total count in "Identifying book X/N"? (3) does a new scan start immediately after the previous one finishes?

**Step 2 — check for a stale command queue backlog:**

A common cause of a persistent full-library scan loop is a large backlog of queued `RescanFoldersCommand` entries in the database with `folders=["/books/"]`, generated by an old bug. Check:
```bash
sqlite3 -readonly /mnt/docker/bookshelf/readarr.db "
SELECT json_extract(Body,'$.folders[0]') as folder, COUNT(*) as cnt, Status
FROM Commands WHERE Name='RescanFolders' GROUP BY folder, Status;"
```
If you see hundreds or thousands of `Status=0` (queued) rows with folder `/books/`, that's the cause. The currently running command has `Status=1`. Fix: mark all stale root-scan queued commands as Cancelled (`Status=5`) so they aren't re-enqueued on restart:
```bash
sqlite3 /mnt/docker/bookshelf/readarr.db "
UPDATE Commands SET Status = 5
WHERE Name = 'RescanFolders' AND Status = 0
  AND json_extract(Body, '$.folders[0]') = '/books/';"
```
Then restart the container. The API also exposes `DELETE /api/v1/command/{id}` but only removes from the in-memory queue — it doesn't persist to the DB, so stale commands come back on restart.

### Metadata providers

- `src/NzbDrone.Core/MetadataSource/Hardcover/` — Hardcover API client (enabled when `HARDCOVER` env var is set)
- `src/NzbDrone.Core/MetadataSource/Goodreads/` — Goodreads API client
- `src/NzbDrone.Core/ImportLists/Hardcover/` and `Goodreads/` — shelf/list import

### Download clients

Standard Readarr clients plus:
- `src/NzbDrone.Core/Download/Clients/Soulseek/` — slskd integration. State strings are compound (e.g. `"Completed, Succeeded"`, `"Queued, Remotely"`). File path is `{downloadDir}/{lastRemoteDirectoryName}/{filename}`.

**slskd removal behaviour**: The slskd API endpoint `DELETE /api/v0/transfers/downloads/{username}/{id}` returns 204 for completed transfers but does not actually remove the record. `SlskdProxy.RemoveTransfer` therefore always follows the individual delete with `DELETE /api/v0/transfers/downloads/all/completed` to actually clear the record.

### CWA (Calibre-Web Automated) integration

There are **two modes** — which one is active depends on whether `CwaIngestFolder` is set in Media Management config:

**CWA-first mode** (active when `Config → Media Management → CWA Ingest Folder` is set):
- `CompletedDownloadService.Import()` intercepts the download **before** the normal Readarr import pipeline
- Moves the downloaded file directly to the CWA ingest folder
- Immediately **unmonitors the affected books** so they aren't re-downloaded if Calibre later reorganises the file
- Publishes `DownloadCompletedEvent` and returns — `ProcessPath` is never called, no `BookFile` records are created from this path
- The `CalibreWebAutomated.OnReleaseImport` notification method does **not** fire in this mode
- CWA/Calibre processes the ingest folder and writes the final ebook to the books library path; the FSW then detects it and Readarr creates a `BookFile` record

**Readarr-first mode** (active when the global ingest folder is cleared and a `CalibreWebAutomated` notification is configured instead):
- Downloads go through the normal Readarr import pipeline → file moved to `/books/Author/Book/`
- `CalibreWebAutomated.OnReleaseImport` fires after import and **copies** the file to the CWA ingest folder
- Readarr owns the `BookFile` record; CWA gets a copy for its Calibre library

The `src/NzbDrone.Core/Notifications/CalibreWebAutomated/` code only executes in Readarr-first mode. In CWA-first mode it is dead code. `src/NzbDrone.Core/Notifications/Hardcover/` marks books as read on Hardcover after import (active in both modes).

## Live deployment

The running instance is at `http://nas.local:8787`. Paths on the NAS host:

| Purpose | Host path |
|---|---|
| Config & DB | `/mnt/docker/bookshelf/` |
| Logs | `/mnt/docker/bookshelf/logs/` |
| Books library | `/mnt/pool/dataset/media/Books/` (mounted as `/books/` inside container) |
| CWA ingest | `/cwa-book-ingest/` (container path, configured in Media Management) |
| slskd complete | `/local/soulseek/complete/` (container path) |

API key is in `/mnt/docker/bookshelf/config.xml` (`<ApiKey>`). Use it with `-H "X-Api-Key: ..."` for API calls.

## Docker / deployment

Images are built and pushed to `ghcr.io/frizat82/bookshelf` by CI on every push to `main`. The `hardcover` tag is the floating latest. To deploy, pull `:hardcover` (not a pinned version tag).

The container exposes port `8787` and requires a `/config` volume. The `METADATA_URL` and `HARDCOVER` env vars control which metadata provider is active.

## Git remotes

- `fork` → `https://github.com/frizat82/bookshelf.git` (push here)
- `origin` → `https://github.com/pennydreadful/bookshelf.git` (upstream — do not push)
- `upstream` → `https://github.com/Readarr/Readarr.git` (original Readarr — do not push)

All changes go to `fork main`. Only the `frizat82` repo has CI that builds and publishes Docker images.
