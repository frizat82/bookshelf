using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Organizer
{
    public static class FileNameValidation
    {
        internal static readonly Regex OriginalTokenRegex = new Regex(@"(\{original[- ._](?:title|filename)\})",
                                                                            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static IRuleBuilderOptions<T, string> ValidBookFormat<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            ruleBuilder.NotEmpty();
            ruleBuilder.SetValidator(new IllegalCharactersValidator());

            return ruleBuilder.SetValidator(new ValidStandardTrackFormatValidator());
        }

        public static IRuleBuilderOptions<T, string> ValidAuthorFolderFormat<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            ruleBuilder.NotEmpty();
            ruleBuilder.SetValidator(new IllegalCharactersValidator());

            return ruleBuilder.Matches(FileNameBuilder.AuthorNameRegex).WithMessage("Must contain Author name");
        }
    }

    public class ValidStandardTrackFormatValidator : PropertyValidator<object, string>
    {
        public override string Name => "ValidStandardTrackFormatValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Must contain Book Title AND PartNumber, OR Original Title";

        public override bool IsValid(ValidationContext<object> context, string value)
        {
            if (value == null)
            {
                return false;
            }

            return (FileNameBuilder.BookTitleRegex.IsMatch(value) && FileNameBuilder.PartRegex.IsMatch(value)) ||
                   FileNameValidation.OriginalTokenRegex.IsMatch(value);
        }
    }

    public class IllegalCharactersValidator : PropertyValidator<object, string>
    {
        private readonly char[] _invalidPathChars = Path.GetInvalidPathChars();

        public override string Name => "IllegalCharactersValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Contains illegal characters: {InvalidCharacters}";

        public override bool IsValid(ValidationContext<object> context, string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return true;
            }

            var invalidCharacters = _invalidPathChars.Where(i => value!.IndexOf(i) >= 0).ToList();
            if (invalidCharacters.Any())
            {
                context.MessageFormatter.AppendArgument("InvalidCharacters", string.Join("", invalidCharacters));
                return false;
            }

            return true;
        }
    }
}
