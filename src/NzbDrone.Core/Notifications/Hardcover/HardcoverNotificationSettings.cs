using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Notifications.Hardcover
{
    public class HardcoverNotificationSettingsValidator : AbstractValidator<HardcoverNotificationSettings>
    {
        public HardcoverNotificationSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
            RuleFor(c => c.RemoveListIds).NotEmpty().When(c => !c.AddListIds.Any());
            RuleFor(c => c.AddListIds).NotEmpty().When(c => !c.RemoveListIds.Any());
        }
    }

    public class HardcoverNotificationSettings : IProviderConfig
    {
        private static readonly HardcoverNotificationSettingsValidator Validator = new ();

        public HardcoverNotificationSettings()
        {
            BaseUrl = "https://api.hardcover.app";
            AddListIds = new string[] { };
            RemoveListIds = new string[] { };
        }

        [FieldDefinition(0, Label = "Base URL", HelpText = "Hardcover API base URL")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "API Key", Privacy = PrivacyLevel.ApiKey, HelpText = "Hardcover personal API key (from Settings > API)")]
        public string ApiKey { get; set; }

        [FieldDefinition(2, Type = FieldType.Select, SelectOptionsProviderAction = "getLists", Label = "Add to Lists", HelpText = "Add imported book to these Hardcover lists")]
        public IEnumerable<string> AddListIds { get; set; }

        [FieldDefinition(3, Type = FieldType.Select, SelectOptionsProviderAction = "getLists", Label = "Remove from Lists", HelpText = "Remove imported book from these Hardcover lists (e.g. a 'Want to Read' list)")]
        public IEnumerable<string> RemoveListIds { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
