using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace NzbDrone.Core.Notifications.CalibreWebAutomated
{
    public class CalibreWebAutomatedSettingsValidator : AbstractValidator<CalibreWebAutomatedSettings>
    {
        public CalibreWebAutomatedSettingsValidator()
        {
            RuleFor(c => c.IngestFolder).IsValidPath();
        }
    }

    public class CalibreWebAutomatedSettings : IProviderConfig
    {
        private static readonly CalibreWebAutomatedSettingsValidator Validator = new ();

        [FieldDefinition(0, Label = "Ingest Folder", Type = FieldType.Path, HelpText = "Path to the Calibre-Web Automated book ingest folder")]
        public string IngestFolder { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
