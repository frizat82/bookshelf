using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Validation.Paths
{
    public class SystemFolderValidator : PropertyValidator<object, string>
    {
        public override string Name => "SystemFolderValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Path '{path}' is {relationship} system folder {systemFolder}";

        public override bool IsValid(ValidationContext<object> context, string value)
        {
            context.MessageFormatter.AppendArgument("path", value);

            foreach (var systemFolder in SystemFolders.GetSystemFolders())
            {
                context.MessageFormatter.AppendArgument("systemFolder", systemFolder);

                if (systemFolder.PathEquals(value))
                {
                    context.MessageFormatter.AppendArgument("relationship", "set to");

                    return false;
                }

                if (systemFolder.IsParentPath(value))
                {
                    context.MessageFormatter.AppendArgument("relationship", "child of");

                    return false;
                }
            }

            return true;
        }
    }
}
