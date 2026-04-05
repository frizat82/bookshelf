using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Validation.Paths
{
    public class RecycleBinValidator : PropertyValidator<object, string>
    {
        private readonly IConfigService _configService;

        public RecycleBinValidator(IConfigService configService)
        {
            _configService = configService;
        }

        public override string Name => "RecycleBinValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Path '{path}' is {relationship} configured recycle bin folder";

        public override bool IsValid(ValidationContext<object> context, string value)
        {
            var recycleBin = _configService.RecycleBin;

            if (value == null || recycleBin.IsNullOrWhiteSpace())
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", value);

            if (recycleBin.PathEquals(value))
            {
                context.MessageFormatter.AppendArgument("relationship", "set to");

                return false;
            }

            if (recycleBin.IsParentPath(value))
            {
                context.MessageFormatter.AppendArgument("relationship", "child of");

                return false;
            }

            return true;
        }
    }
}
