using System.Collections.Generic;
using System.IO;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications.CalibreWebAutomated
{
    public class CalibreWebAutomated : NotificationBase<CalibreWebAutomatedSettings>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly Logger _logger;

        public override string Name => "Calibre-Web Automated";
        public override string Link => "https://github.com/crocodilestick/Calibre-Web-Automated";

        public override ProviderMessage Message => new ProviderMessage(
            "Imported book files will be moved to the configured ingest folder for Calibre-Web Automated to process. Readarr will mark the book as 'have' after CWA imports it back to your library.",
            ProviderMessageType.Info);

        public CalibreWebAutomated(IDiskProvider diskProvider,
                                   IDiskTransferService diskTransferService,
                                   Logger logger)
        {
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _logger = logger;
        }

        public override void OnReleaseImport(BookDownloadMessage message)
        {
            foreach (var bookFile in message.BookFiles)
            {
                var fileName = Path.GetFileName(bookFile.Path);
                var destination = Path.Combine(Settings.IngestFolder, fileName);

                _logger.Debug("CWA: Copying '{0}' to ingest folder '{1}'", bookFile.Path, destination);

                _diskTransferService.TransferFile(bookFile.Path, destination, TransferMode.Move, overwrite: true);

                _logger.Info("CWA: Copied '{0}' to '{1}'", fileName, Settings.IngestFolder);
            }
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            if (!_diskProvider.FolderExists(Settings.IngestFolder))
            {
                failures.Add(new ValidationFailure(nameof(Settings.IngestFolder), $"Ingest folder does not exist: {Settings.IngestFolder}"));
            }

            return new ValidationResult(failures);
        }
    }
}
