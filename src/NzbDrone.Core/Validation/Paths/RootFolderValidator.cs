using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Validation.Paths
{
    public class RootFolderValidator : PropertyValidator<object, string>
    {
        private readonly IRootFolderService _rootFolderService;

        public RootFolderValidator(IRootFolderService rootFolderService)
        {
            _rootFolderService = rootFolderService;
        }

        public override string Name => "RootFolderValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Path '{path}' is already configured as a root folder";

        public override bool IsValid(ValidationContext<object> context, string value)
        {
            if (value == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", value);

            return !_rootFolderService.All().Exists(r => r.Path.PathEquals(value));
        }
    }
}
