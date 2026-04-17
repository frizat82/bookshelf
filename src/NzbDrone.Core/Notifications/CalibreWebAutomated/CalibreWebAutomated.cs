using System.Collections.Generic;
using FluentValidation.Results;
using NzbDrone.Common.Disk;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications.CalibreWebAutomated
{
    public class CalibreWebAutomated : NotificationBase<CalibreWebAutomatedSettings>
    {
        private readonly IDiskProvider _diskProvider;

        public override string Name => "Calibre-Web Automated";
        public override string Link => "https://github.com/crocodilestick/Calibre-Web-Automated";

        public override ProviderMessage Message => new ProviderMessage(
            "Completed downloads will be moved to the configured ingest folder before Readarr imports them. CWA then imports the book into your Calibre library. Readarr picks it up on the next scan.",
            ProviderMessageType.Info);

        public CalibreWebAutomated(IDiskProvider diskProvider)
        {
            _diskProvider = diskProvider;
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
