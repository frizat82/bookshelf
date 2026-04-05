using System.Collections.Generic;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;

namespace Readarr.Http.Validation
{
    public class EmptyCollectionValidator<T> : PropertyValidator<object, IEnumerable<T>>
    {
        public override string Name => "EmptyCollectionValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Collection Must Be Empty";

        public override bool IsValid(ValidationContext<object> context, IEnumerable<T> value)
        {
            if (value == null)
            {
                return true;
            }

            return value.Empty();
        }
    }
}
