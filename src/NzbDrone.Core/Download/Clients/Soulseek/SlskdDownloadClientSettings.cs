using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Soulseek
{
    public class SlskdDownloadClientSettingsValidator : AbstractValidator<SlskdDownloadClientSettings>
    {
        public SlskdDownloadClientSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    public class SlskdDownloadClientSettings : IProviderConfig
    {
        private static readonly SlskdDownloadClientSettingsValidator Validator = new ();

        public SlskdDownloadClientSettings()
        {
            BaseUrl = string.Empty;
            ApiKey = string.Empty;
        }

        [FieldDefinition(0, Label = "URL", HelpText = "URL of your slskd instance (e.g. http://nas.local:5030)")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "API Key", Type = FieldType.Password, HelpText = "API key from slskd Settings → General")]
        public string ApiKey { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
