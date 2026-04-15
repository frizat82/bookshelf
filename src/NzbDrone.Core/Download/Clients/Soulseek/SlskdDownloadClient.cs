using System;
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

            var hasCompleted = allTransfers.Any(u => u.Directories.Any(d => d.Files.Any(f => f.Direction == "Download" && f.State == "Completed")));
            var downloadDir = hasCompleted ? _proxy.GetDownloadDirectory(Settings) : null;

            foreach (var userGroup in allTransfers)
            {
                foreach (var dir in userGroup.Directories)
                {
                    foreach (var transfer in dir.Files.Where(t => t.Direction == "Download"))
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
                            CanMoveFiles = transfer.State == "Completed",
                            CanBeRemoved = transfer.State is "Completed" or "Cancelled" or "Errored"
                                                                          or "TimedOut" or "Rejected" or "Aborted",
                        };

                        if (transfer.RemainingSeconds.HasValue)
                        {
                            item.RemainingTime = TimeSpan.FromSeconds(transfer.RemainingSeconds.Value);
                        }

                        if (transfer.State == "Completed" && !string.IsNullOrWhiteSpace(downloadDir))
                        {
                            var relativePath = transfer.Filename
                                .Replace('\\', Path.DirectorySeparatorChar)
                                .TrimStart(Path.DirectorySeparatorChar);
                            item.OutputPath = new OsPath(Path.Combine(downloadDir, userGroup.Username, relativePath));
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

            _proxy.RemoveTransfer(Settings, parts[0], parts[1], deleteData);
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

        private static DownloadItemStatus MapStatus(string state) => state switch
        {
            "Requested" or "Initializing" => DownloadItemStatus.Queued,
            "InProgress" => DownloadItemStatus.Downloading,
            "Completed" => DownloadItemStatus.Completed,
            _ => DownloadItemStatus.Failed,
        };
    }
}
