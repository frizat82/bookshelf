using FluentValidation;
using FluentValidation.Results;
using FluentValidation.Validators;

namespace NzbDrone.Core.Validation
{
    /// <summary>
    /// Extension methods for using PropertyValidator&lt;object, TProperty&gt; with SetValidator,
    /// which requires matching model types in FluentValidation 11.
    /// </summary>
    public static class PropertyValidatorExtensions
    {
        public static IRuleBuilderOptions<T, TProperty> SetValidator<T, TProperty>(
            this IRuleBuilder<T, TProperty> ruleBuilder,
            PropertyValidator<object, TProperty> validator)
        {
            return (IRuleBuilderOptions<T, TProperty>)ruleBuilder.Custom((value, context) =>
            {
                var innerContext = new ValidationContext<object>(context.InstanceToValidate);
                var isValid = validator.IsValid(innerContext, value);

                if (!isValid)
                {
                    innerContext.MessageFormatter.AppendArgument("PropertyValue", value?.ToString());
                    innerContext.MessageFormatter.AppendArgument("PropertyName", context.DisplayName);

                    var template = ((IPropertyValidator)validator).GetDefaultMessageTemplate(validator.Name);
                    var message = innerContext.MessageFormatter.BuildMessage(template);

                    context.AddFailure(new ValidationFailure(context.PropertyPath, message));
                }
            });
        }
    }
}
