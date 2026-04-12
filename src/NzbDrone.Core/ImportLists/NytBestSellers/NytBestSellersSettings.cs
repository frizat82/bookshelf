using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.NytBestSellers
{
    public class NytBestSellersSettingsValidator : AbstractValidator<NytBestSellersSettings>
    {
    }

    public class NytBestSellersSettings : IImportListSettings
    {
        private static readonly NytBestSellersSettingsValidator Validator = new ();

        public NytBestSellersSettings()
        {
            BaseUrl = "https://www.nytimes.com";
            IncludeFiction = true;
            IncludeNonFiction = true;
            IncludeCombined = false;
        }

        public string BaseUrl { get; set; }

        [FieldDefinition(0, Label = "Hardcover Fiction", Type = FieldType.Checkbox, HelpText = "Include NYT Hardcover Fiction list")]
        public bool IncludeFiction { get; set; }

        [FieldDefinition(1, Label = "Hardcover Nonfiction", Type = FieldType.Checkbox, HelpText = "Include NYT Hardcover Nonfiction list")]
        public bool IncludeNonFiction { get; set; }

        [FieldDefinition(2, Label = "Combined Print & E-Book Fiction", Type = FieldType.Checkbox, HelpText = "Include NYT Combined Print and E-Book Fiction list")]
        public bool IncludeCombined { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
