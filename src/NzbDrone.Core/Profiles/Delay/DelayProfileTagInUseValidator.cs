using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Profiles.Delay
{
    public class DelayProfileTagInUseValidator : PropertyValidator<object, object>
    {
        private readonly IDelayProfileService _delayProfileService;

        public DelayProfileTagInUseValidator(IDelayProfileService delayProfileService)
        {
            _delayProfileService = delayProfileService;
        }

        public override string Name => "DelayProfileTagInUseValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "One or more tags is used in another profile";

        public override bool IsValid(ValidationContext<object> context, object value)
        {
            if (value == null)
            {
                return true;
            }

            dynamic instance = context.InstanceToValidate;
            var instanceId = (int)instance.Id;

            if (value is not HashSet<int> collection || collection.Empty())
            {
                return true;
            }

            return _delayProfileService.All().None(d => d.Id != instanceId && d.Tags.Intersect(collection).Any());
        }
    }
}
