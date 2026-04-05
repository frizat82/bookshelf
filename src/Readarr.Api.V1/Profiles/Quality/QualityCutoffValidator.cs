using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Validators;

namespace Readarr.Api.V1.Profiles.Quality
{
    public static class QualityCutoffValidator
    {
        public static IRuleBuilderOptions<T, int> ValidCutoff<T>(this IRuleBuilder<T, int> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new ValidCutoffValidator<T>());
        }
    }

    public class ValidCutoffValidator<T> : PropertyValidator<T, int>
    {
        public override string Name => "ValidCutoffValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Cutoff must be an allowed quality or group";

        public override bool IsValid(ValidationContext<T> context, int value)
        {
            dynamic instance = context.InstanceToValidate;
            var items = instance.Items as IList<QualityProfileQualityItemResource>;

            var cutoffItem = items?.SingleOrDefault(i => (i.Quality == null && i.Id == value) || i.Quality?.Id == value);

            return cutoffItem is { Allowed: true };
        }
    }
}
