using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IRootFolderWatchingService
    {
        void ReportFileSystemChangeBeginning(params string[] paths);
    }

    public sealed class RootFolderWatchingService : IRootFolderWatchingService,
        IDisposable,
        IHandle<ModelEvent<RootFolder>>,
        IHandle<ApplicationStartedEvent>,
        IHandle<ConfigSavedEvent>
    {
        private const int DEBOUNCE_TIMEOUT_SECONDS = 30;

        private static readonly char[] PathSeparators = { '/', '\\' };

        private readonly ConcurrentDictionary<string, FileSystemWatcher> _fileSystemWatchers = new ConcurrentDictionary<string, FileSystemWatcher>();
        // Used as a concurrent set; only keys matter, so value type is byte to minimise footprint.
        private ConcurrentDictionary<string, byte> _tempIgnoredPaths = new ConcurrentDictionary<string, byte>();
        private ConcurrentDictionary<string, string> _changedPaths = new ConcurrentDictionary<string, string>();

        private readonly IRootFolderService _rootFolderService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        private readonly Debouncer _scanDebouncer;
        private bool _watchForChanges;

        public RootFolderWatchingService(IRootFolderService rootFolderService,
                                         IManageCommandQueue commandQueueManager,
                                         IConfigService configService,
                                         Logger logger)
        {
            _rootFolderService = rootFolderService;
            _commandQueueManager = commandQueueManager;
            _configService = configService;
            _logger = logger;

            _scanDebouncer = new Debouncer(ScanPending, TimeSpan.FromSeconds(DEBOUNCE_TIMEOUT_SECONDS), true);
        }

        public void Dispose()
        {
            foreach (var watcher in _fileSystemWatchers.Values)
            {
                DisposeWatcher(watcher, false);
            }
        }

        public void ReportFileSystemChangeBeginning(params string[] paths)
        {
            foreach (var path in paths.Where(x => x.IsNotNullOrWhiteSpace()))
            {
                _logger.Trace($"reporting start of change to {path}");
                _tempIgnoredPaths.TryAdd(path.CleanFilePathBasic(), 0);
            }
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _watchForChanges = _configService.WatchLibraryForChanges;

            if (_watchForChanges)
            {
                _rootFolderService.All().ForEach(x => StartWatchingPath(x.Path));
            }
        }

        public void Handle(ConfigSavedEvent message)
        {
            var oldWatch = _watchForChanges;
            _watchForChanges = _configService.WatchLibraryForChanges;

            if (_watchForChanges != oldWatch)
            {
                if (_watchForChanges)
                {
                    _rootFolderService.All().ForEach(x => StartWatchingPath(x.Path));
                }
                else
                {
                    _rootFolderService.All().ForEach(x => StopWatchingPath(x.Path));
                }
            }
        }

        public void Handle(ModelEvent<RootFolder> message)
        {
            if (message.Action == ModelAction.Created && _watchForChanges)
            {
                StartWatchingPath(message.Model.Path);
            }
            else if (message.Action == ModelAction.Deleted)
            {
                StopWatchingPath(message.Model.Path);
            }
        }

        private void StartWatchingPath(string path)
        {
            Ensure.That(path, () => path).IsNotNullOrWhiteSpace();
            Ensure.That(path, () => path).IsValidPath(PathValidationType.CurrentOs);

            // Already being watched
            if (_fileSystemWatchers.ContainsKey(path))
            {
                return;
            }

            // Creating a FileSystemWatcher over the LAN can take hundreds of milliseconds, so wrap it in a Task to do them all in parallel
            Task.Run(() =>
            {
                try
                {
                    var newWatcher = new FileSystemWatcher(path, "*")
                    {
                        IncludeSubdirectories = true,
                        InternalBufferSize = 65536,
                        NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite
                    };

                    newWatcher.Created += Watcher_Changed;
                    newWatcher.Deleted += Watcher_Changed;
                    newWatcher.Renamed += Watcher_Changed;
                    newWatcher.Changed += Watcher_Changed;
                    newWatcher.Error += Watcher_Error;

                    if (_fileSystemWatchers.TryAdd(path, newWatcher))
                    {
                        newWatcher.EnableRaisingEvents = true;
                        _logger.Info("Watching directory {0}", path);
                    }
                    else
                    {
                        DisposeWatcher(newWatcher, false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error watching path: {0}", path);
                }
            });
        }

        private void StopWatchingPath(string path)
        {
            if (_fileSystemWatchers.TryGetValue(path, out var watcher))
            {
                DisposeWatcher(watcher, true);
            }
        }

        private void Watcher_Error(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            var dw = (FileSystemWatcher)sender;

            if (ex is InternalBufferOverflowException)
            {
                _logger.Warn("File system watcher buffer overflow for: {0}. Events were lost but existing queued changes will still be processed.", dw.Path);

                // Do NOT queue a full root-folder rescan here. The overflow means we missed
                // some events, but queuing a scan of the entire library (7000+ books) for
                // every overflow causes an endless rescan loop when many files change at once
                // (e.g. CWA processing a batch of downloads). Individual change events that
                // arrived before the overflow are already in _changedPaths and will be scoped
                // to their author subfolders. The 24-hour scheduled rescan will catch anything
                // that was truly missed.
            }
            else
            {
                _logger.Error(ex, "Error in Directory watcher for: {0}", dw.Path);

                DisposeWatcher(dw, true);
            }
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            try
            {
                var rootFolder = ((FileSystemWatcher)sender).Path;
                var path = e.FullPath;

                if (path.IsNullOrWhiteSpace())
                {
                    throw new ArgumentNullException("path");
                }

                _changedPaths.TryAdd(path, rootFolder);

                _scanDebouncer.Execute();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception in ReportFileSystemChanged. Path: {0}", e.FullPath);
            }
        }

        private void ScanPending()
        {
            // Swap out the dictionaries atomically to avoid a TOCTOU window where
            // Watcher_Changed adds an entry after snapshot but before clear.
            var pairs = Interlocked.Exchange(ref _changedPaths, new ConcurrentDictionary<string, string>());
            var ignored = Interlocked.Exchange(ref _tempIgnoredPaths, new ConcurrentDictionary<string, byte>()).Keys.ToArray();

            var toScan = new HashSet<string>();

            foreach (var item in pairs)
            {
                var path = item.Key.CleanFilePathBasic();
                var rootFolder = item.Value;

                if (!ShouldIgnoreChange(path, ignored))
                {
                    // Use manual string ops rather than IsParentPath/GetRelativePath — those
                    // create DirectoryInfo objects and hit disk, too slow for a tight event loop.
                    var cleanRoot = rootFolder.CleanFilePathBasic();
                    var relative = path.StartsWith(cleanRoot, StringComparison.OrdinalIgnoreCase)
                        ? path.Substring(cleanRoot.Length).TrimStart('/', '\\')
                        : null;
                    var topLevel = relative?.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

                    if (topLevel == null)
                    {
                        // Event is for the root directory itself (e.g. LastWrite when a child dir
                        // is created). Don't fall back to a full root scan — child events for the
                        // actual author subfolders are already in the snapshot.
                        _logger.Trace("Skipping root-level change event for {0}", path);
                    }
                    else
                    {
                        _logger.Trace("Actioning change to {0}", path);
                        toScan.Add(Path.Combine(rootFolder, topLevel));
                    }
                }
                else
                {
                    _logger.Trace("Ignoring change to {0}", path);
                }
            }

            if (toScan.Any())
            {
                // Known (not Matched) because watcher events can surface brand-new files that
                // don't yet exist in the database; we need to import them, not just re-evaluate
                // existing records. Post-refresh rescans use Matched instead.
                _commandQueueManager.Push(new RescanFoldersCommand(toScan.ToList(), FilterFilesType.Known, false, null));
            }
        }

        private bool ShouldIgnoreChange(string cleanPath, string[] ignoredPaths)
        {
            // Skip partial/backup
            if (cleanPath.EndsWith(".partial~") ||
                cleanPath.EndsWith(".backup~"))
            {
                return true;
            }

            // only proceed for directories and files with music extensions
            var extension = Path.GetExtension(cleanPath);
            if (extension.IsNullOrWhiteSpace() && !Directory.Exists(cleanPath))
            {
                return true;
            }

            if (extension.IsNotNullOrWhiteSpace() && !MediaFileExtensions.AllExtensions.Contains(extension))
            {
                return true;
            }

            // If the parent of an ignored path has a change event, ignore that too
            // Note that we can't afford to use the PathEquals or IsParentPath functions because
            // these rely on disk access which is too slow when trying to handle many update events
            return ignoredPaths.Any(i => i.Equals(cleanPath, DiskProviderBase.PathStringComparison) ||
                                    i.StartsWith(cleanPath + Path.DirectorySeparatorChar, DiskProviderBase.PathStringComparison) ||
                                    Path.GetDirectoryName(i)?.Equals(cleanPath, DiskProviderBase.PathStringComparison) == true);
        }

        private void DisposeWatcher(FileSystemWatcher watcher, bool removeFromList)
        {
            try
            {
                using (watcher)
                {
                    _logger.Info("Stopping directory watching for path {0}", watcher.Path);

                    watcher.Created -= Watcher_Changed;
                    watcher.Deleted -= Watcher_Changed;
                    watcher.Renamed -= Watcher_Changed;
                    watcher.Changed -= Watcher_Changed;
                    watcher.Error -= Watcher_Error;

                    try
                    {
                        watcher.EnableRaisingEvents = false;
                    }
                    catch (InvalidOperationException)
                    {
                        // Seeing this under mono on linux sometimes
                        // Collection was modified; enumeration operation may not execute.
                    }
                }
            }
            catch
            {
                // we don't care about exceptions disposing
            }
            finally
            {
                if (removeFromList)
                {
                    _fileSystemWatchers.TryRemove(watcher.Path, out _);
                }
            }
        }
    }
}
