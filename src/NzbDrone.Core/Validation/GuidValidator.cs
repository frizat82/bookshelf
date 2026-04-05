using System;
using FluentValidation;
using FluentValidation.Validators;

namespace NzbDrone.Core.Validation
{
    public class GuidValidator : PropertyValidator<object, string>
    {
        public override string Name => "GuidValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "String is not a valid Guid";

        public override bool IsValid(ValidationContext<object> context, string value)
        {
            if (value == null)
            {
                return false;
            }

            return Guid.TryParse(value, out _);
        }
    }
}
