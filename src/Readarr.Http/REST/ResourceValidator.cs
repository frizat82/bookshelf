using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FluentValidation;
using Readarr.Http.ClientSchema;

namespace Readarr.Http.REST
{
    public class ResourceValidator<TResource> : AbstractValidator<TResource>
    {
        public IRuleBuilderInitial<TResource, TProperty> RuleForField<TProperty>(Expression<Func<TResource, IEnumerable<Field>>> fieldListAccessor, string fieldName)
        {
            var compiledAccessor = fieldListAccessor.Compile();
            return RuleFor(resource => (TProperty)GetValue(resource, compiledAccessor, fieldName));
        }

        private static object GetValue(TResource container, Func<TResource, IEnumerable<Field>> fieldListAccessor, string fieldName)
        {
            var resource = fieldListAccessor(container)?.SingleOrDefault(c => c.Name == fieldName);
            return resource?.Value;
        }
    }
}
