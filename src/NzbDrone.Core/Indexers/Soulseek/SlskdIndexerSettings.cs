using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Soulseek
{
    public class SlskdIndexerSettingsValidator : AbstractValidator<SlskdIndexerSettings>
    {
        public SlskdIndexerSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    public class SlskdIndexerSettings : IIndexerSettings
    {
        private static readonly SlskdIndexerSettingsValidator Validator = new ();

        public SlskdIndexerSettings()
        {
            BaseUrl = string.Empty;
            ApiKey = string.Empty;
            SearchTimeout = 15;
            FileLimit = 10000;
            ResponseLimit = 500;
            AllowedFormats = new List<string> { "epub", "mobi", "azw3", "pdf" };
            AuthorMinScore = 80;
            TitleMinScore = 75;
        }

        [FieldDefinition(0, Label = "URL", HelpText = "URL of your slskd instance (e.g. http://nas.local:5030)")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "API Key", Type = FieldType.Password, HelpText = "API key from slskd Settings → General")]
        public string ApiKey { get; set; }

        [FieldDefinition(2, Label = "Search Timeout (s)", Type = FieldType.Number, HelpText = "How long to wait for Soulseek peers to respond (seconds)", Advanced = true)]
        public int SearchTimeout { get; set; }

        [FieldDefinition(3, Label = "File Limit", Type = FieldType.Number, HelpText = "Maximum number of files to return from a search", Advanced = true)]
        public int FileLimit { get; set; }

        [FieldDefinition(4, Label = "Response Limit", Type = FieldType.Number, HelpText = "Maximum number of peer responses per search", Advanced = true)]
        public int ResponseLimit { get; set; }

        [FieldDefinition(5, Label = "Author Match Threshold", Type = FieldType.Number, HelpText = "Minimum fuzzy match score (0-100) for author name", Advanced = true)]
        public int AuthorMinScore { get; set; }

        [FieldDefinition(6, Label = "Title Match Threshold", Type = FieldType.Number, HelpText = "Minimum fuzzy match score (0-100) for book title", Advanced = true)]
        public int TitleMinScore { get; set; }

        public List<string> AllowedFormats { get; set; }

        // IIndexerSettings
        public int? EarlyReleaseLimit { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
