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
        private readonly Logger _logger;

        public override string Name => "Calibre-Web Automated";
        public override string Link => "https://github.com/crocodilestick/Calibre-Web-Automated";

        public override ProviderMessage Message => new ProviderMessage(
            "After Readarr imports a book, a copy is placed in the configured ingest folder. Calibre-Web Automated then processes it into your Calibre library.",
            ProviderMessageType.Info);

        public CalibreWebAutomated(IDiskProvider diskProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public override void OnReleaseImport(BookDownloadMessage message)
        {
            foreach (var bookFile in message.BookFiles)
            {
                var fileName = Path.GetFileName(bookFile.Path);
                var destination = Path.Combine(Settings.IngestFolder, fileName);

                _logger.Debug("Copying '{0}' to CWA ingest folder '{1}'", bookFile.Path, destination);

                _diskProvider.CopyFile(bookFile.Path, destination, overwrite: true);
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
