# Soulseek / slskd Integration Plan

Based on porting Tubifarry (MIT licensed, C# .NET) — compatible with GPL v3.

## Architecture

Two new components mirroring Tubifarry's native plugin approach:

### 1. Indexer — `src/NzbDrone.Core/Indexers/Soulseek/`

| File | Purpose |
|------|---------|
| `SlskdIndexer.cs` | Implements `IIndexer`, searches slskd and returns results to Readarr's normal search pipeline |
| `SlskdIndexerSettings.cs` | Base URL, API key, format preferences (epub/mobi/azw3/pdf), fuzzy match thresholds |
| `SlskdRequestGenerator.cs` | Builds author+title search queries, POSTs to `/api/v0/searches`, handles async polling |
| `SlskdParser.cs` | Filters by extension whitelist, fuzzy-matches author+title via FuzzySharp, scores and sorts results |

### 2. Download Client — `src/NzbDrone.Core/Download/Clients/Soulseek/`

| File | Purpose |
|------|---------|
| `SlskdDownloadClient.cs` | Implements `IDownloadClient`, enqueues downloads, monitors transfer status |
| `SlskdDownloadClientSettings.cs` | Base URL, API key, download path (maps to slskd's configured download dir) |
| `SlskdProxy.cs` | HTTP client wrapping all slskd API endpoints |

## slskd API Endpoints

| Action | Method | Endpoint |
|--------|--------|----------|
| Test connection | GET | `/api/v0/application` |
| Create search | POST | `/api/v0/searches` |
| Poll search | GET | `/api/v0/searches/{id}?includeResponses=true` |
| Cancel search | PUT | `/api/v0/searches/{id}` |
| Delete search | DELETE | `/api/v0/searches/{id}` |
| Enqueue download | POST | `/api/v0/transfers/downloads/{username}` |
| Monitor transfer | GET | `/api/v0/transfers/downloads/{username}/{id}` |
| Get all transfers | GET | `/api/v0/transfers/downloads?includeRemoved=true` |
| Clear completed | DELETE | `/api/v0/transfers/downloads/all/completed` |
| Get user directory | POST | `/api/v0/users/{username}/directory` |
| Get options/paths | GET | `/api/v0/options` |

Auth: `X-API-Key: <key>` header on all requests.

## Search Request Body

```json
{
  "Id": "<guid>",
  "SearchText": "Author Title",
  "SearchTimeout": 15000,
  "FileLimit": 10000,
  "FilterResponses": true,
  "MaximumPeerQueueLength": 100,
  "MinimumPeerUploadSpeed": 0,
  "MinimumResponseFileCount": 1,
  "ResponseLimit": 500
}
```

## Async Search Flow (from Tubifarry)

1. POST search with guid
2. 2-second initial delay
3. Adaptive quadratic polling: `delay = clamp(16p² - 16p + 5, 0.5s, 5s)` where p = progress ratio
4. 20-second grace period after timeout before abort
5. Fetch responses with `includeResponses=true`

## Download Enqueue Body

```json
[{"Filename": "/path/to/file.epub", "Size": 12345678}]
```

## Changes from Tubifarry (music → books)

| Tubifarry (music) | Bookshelf (books) |
|-------------------|-------------------|
| `[flac, mp3, aac, ogg]` | `[epub, mobi, azw3, pdf, djvu]` |
| `artist + album` query | `author + title` query |
| Track count validation | Single file per result |
| Bitrate/sample rate scoring | Format preference order (epub > mobi > azw3 > pdf) |
| `AudioFormatHelper` | Book format detection by extension |
| Lidarr `ReleaseInfo` fields | Readarr `BookInfo` / `RemoteBook` fields |

FuzzySharp thresholds unchanged — author/title fuzzy matching is the same problem as artist/album.

## NuGet Dependency

Add to `src/Directory.Packages.props`:
```xml
<PackageVersion Include="FuzzySharp" Version="2.0.2" />
```

## Result Scoring / Selection Pipeline

1. Filter: `HasFreeUploadSlot`, upload speed, queue length
2. Fuzzy match: author (partial ≥90, token-sort ≥85), title (partial ≥85, token-sort ≥80)
3. Filter: extension whitelist only
4. Score by: format preference + user reputation
5. Sort descending, take top result

## slskd Instance

- URL: `http://nas.local:5030`
- Already connected and logged in as `frizatbooks`
- API key required (check slskd Settings → General)
