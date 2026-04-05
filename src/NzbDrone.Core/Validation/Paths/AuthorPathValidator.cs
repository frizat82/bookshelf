using System.Linq;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Validation.Paths
{
    public class AuthorPathValidator : PropertyValidator<object, string>
    {
        private readonly IAuthorService _authorService;

        public AuthorPathValidator(IAuthorService authorService)
        {
            _authorService = authorService;
        }

        public override string Name => "AuthorPathValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Path '{path}' is already configured for another author";

        public override bool IsValid(ValidationContext<object> context, string value)
        {
            if (value == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", value);

            dynamic instance = context.InstanceToValidate;
            var instanceId = (int)instance.Id;

            return !_authorService.AllAuthorPaths().Any(s => s.Value.PathEquals(value) && s.Key != instanceId);
        }
    }
}
