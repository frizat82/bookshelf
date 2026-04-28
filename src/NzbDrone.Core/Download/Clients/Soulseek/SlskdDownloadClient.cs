using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Soulseek;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Download.Clients.Soulseek
{
    public class SlskdDownloadClient : DownloadClientBase<SlskdDownloadClientSettings>
    {
        private readonly ISlskdProxy _proxy;

        // Belt-and-suspenders: track removed IDs in memory so GetItems() stops returning
        // them immediately even before the next slskd API round-trip confirms the deletion.
        private readonly ConcurrentDictionary<string, byte> _removedTransferIds = new ();

        public override string Name => "Soulseek (slskd)";
        public override DownloadProtocol Protocol => DownloadProtocol.Soulseek;

        public SlskdDownloadClient(ISlskdProxy proxy,
                                   IConfigService configService,
                                   IDiskProvider diskProvider,
                                   IRemotePathMappingService remotePathMappingService,
                                   Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _proxy = proxy;
        }

        public override Task<string> Download(RemoteBook remoteBook, IIndexer indexer)
        {
            var downloadUrl = remoteBook.Release.DownloadUrl;
            SlskdDownloadData data;

            try
            {
                data = SlskdDownloadData.FromDownloadUrl(downloadUrl);
            }
            catch (Exception ex)
            {
                throw new DownloadClientException("Failed to decode Soulseek download URL: " + ex.Message);
            }

            _logger.Debug("Soulseek: Enqueueing '{0}' from '{1}'", Path.GetFileName(data.Filename), data.Username);

            var downloadId = _proxy.EnqueueDownload(Settings, data.Username, data.Filename, data.Size);

            if (string.IsNullOrWhiteSpace(downloadId))
            {
                throw new DownloadClientException("slskd did not return a transfer ID for the enqueued download.");
            }

            return Task.FromResult(downloadId);
        }

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            var allTransfers = _proxy.GetTransfers(Settings);
            var items = new List<DownloadClientItem>();

            var hasCompleted = allTransfers.Any(u => u.Directories.Any(d => d.Files.Any(f => f.Direction == "Download" && f.State != null && f.State.StartsWith("Completed"))));
            var downloadDir = hasCompleted ? _proxy.GetDownloadDirectory(Settings) : null;

            foreach (var userGroup in allTransfers)
            {
                foreach (var dir in userGroup.Directories)
                {
                    // slskd stores files at {downloadDir}/{lastDirComponent}/{filename}
                    // The full remote path is NOT preserved — only the immediate parent directory is.
                    var lastDirComponent = dir.Directory?
                        .Replace('\\', '/')
                        .Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .LastOrDefault();

                    foreach (var transfer in dir.Files.Where(t => t.Direction == "Download" && IsBookFile(t.Filename) && !_removedTransferIds.ContainsKey(t.Id)))
                    {
                        var item = new DownloadClientItem
                        {
                            DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, false),
                            DownloadId = $"{userGroup.Username}/{transfer.Id}",
                            Title = Path.GetFileNameWithoutExtension(
                                transfer.Filename.Replace('\\', '/').Split('/').Last()),
                            TotalSize = transfer.Size,
                            RemainingSize = transfer.Size - transfer.BytesTransferred,
                            Status = MapStatus(transfer.State),
                            CanMoveFiles = transfer.State != null && transfer.State.StartsWith("Completed"),
                            CanBeRemoved = transfer.State != null && (transfer.State.StartsWith("Completed") || transfer.State.StartsWith("Cancelled")
                                                                      || transfer.State.StartsWith("Errored") || transfer.State.StartsWith("TimedOut")
                                                                      || transfer.State.StartsWith("Rejected") || transfer.State.StartsWith("Aborted")),
                        };

                        if (transfer.RemainingSeconds.HasValue)
                        {
                            item.RemainingTime = TimeSpan.FromSeconds(transfer.RemainingSeconds.Value);
                        }

                        if (transfer.State != null && transfer.State.StartsWith("Completed") && !string.IsNullOrWhiteSpace(downloadDir))
                        {
                            var filename = Path.GetFileName(transfer.Filename.Replace('\\', '/'));
                            item.OutputPath = lastDirComponent != null
                                ? new OsPath(Path.Combine(downloadDir, lastDirComponent, filename))
                                : new OsPath(Path.Combine(downloadDir, filename));
                        }

                        if (item.Status == DownloadItemStatus.Failed)
                        {
                            item.Message = $"Transfer state: {transfer.State}";
                        }

                        items.Add(item);
                    }
                }
            }

            return items;
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            var parts = item.DownloadId.Split('/', 2);
            if (parts.Length != 2)
            {
                return;
            }

            var transferId = parts[1];

            // Mark as removed before the API call so GetItems() stops returning this
            // transfer immediately, even if slskd silently ignores the delete request.
            _removedTransferIds.TryAdd(transferId, 0);
            _logger.Info("slskd: Removed transfer '{0}' (id={1}, deleteFile={2})", item.Title, transferId, deleteData);

            _proxy.RemoveTransfer(Settings, parts[0], transferId, deleteData);
        }

        public void CleanCompletedTransfers()
        {
            _logger.Debug("slskd: Clearing all completed transfers from history");
            _proxy.DeleteAllCompleted(Settings);
            _removedTransferIds.Clear();
        }

        public override DownloadClientInfo GetStatus()
        {
            var downloadDir = _proxy.GetDownloadDirectory(Settings);

            var status = new DownloadClientInfo
            {
                IsLocalhost = Settings.BaseUrl.Contains("localhost") || Settings.BaseUrl.Contains("127.0.0.1"),
                RemovesCompletedDownloads = false,
            };

            if (!string.IsNullOrWhiteSpace(downloadDir))
            {
                status.OutputRootFolders.Add(new OsPath(downloadDir));
            }

            return status;
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                _proxy.TestConnection(Settings);
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Cannot connect to slskd: {ex.Message}"));
            }
        }

        private static readonly HashSet<string> BookExtensions = new (StringComparer.OrdinalIgnoreCase)
        {
            ".epub", ".mobi", ".azw3", ".pdf"
        };

        private static bool IsBookFile(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return false;
            }

            var ext = Path.GetExtension(filename.Replace('\\', '/'));
            return BookExtensions.Contains(ext);
        }

        private static DownloadItemStatus MapStatus(string state)
        {
            if (state == null)
            {
                return DownloadItemStatus.Failed;
            }

            // Only "Completed, Succeeded" is a real completion; Rejected/TimedOut/Errored/Cancelled are failures.
            if (state.StartsWith("Completed, Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                return DownloadItemStatus.Completed;
            }

            if (state.StartsWith("Completed", StringComparison.OrdinalIgnoreCase))
            {
                return DownloadItemStatus.Failed;
            }

            // "Queued, Remotely" means waiting in the peer's upload queue — still in progress.
            if (state.StartsWith("Queued", StringComparison.OrdinalIgnoreCase) ||
                state.Equals("Requested", StringComparison.OrdinalIgnoreCase) ||
                state.Equals("Initializing", StringComparison.OrdinalIgnoreCase))
            {
                return DownloadItemStatus.Queued;
            }

            if (state.StartsWith("InProgress", StringComparison.OrdinalIgnoreCase))
            {
                return DownloadItemStatus.Downloading;
            }

            return DownloadItemStatus.Failed;
        }
    }
}
