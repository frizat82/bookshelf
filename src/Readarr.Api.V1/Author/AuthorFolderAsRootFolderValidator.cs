using System;
using System.IO;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;

namespace Readarr.Api.V1.Author
{
    public class AuthorFolderAsRootFolderValidator : PropertyValidator<object, string>
    {
        private readonly IBuildFileNames _fileNameBuilder;

        public AuthorFolderAsRootFolderValidator(IBuildFileNames fileNameBuilder)
        {
            _fileNameBuilder = fileNameBuilder;
        }

        public override string Name => "AuthorFolderAsRootFolderValidator";

        protected override string GetDefaultMessageTemplate(string errorCode) => "Root folder path '{rootFolderPath}' contains author folder '{authorFolder}'";

        public override bool IsValid(ValidationContext<object> context, string value)
        {
            if (value == null)
            {
                return true;
            }

            if (context.InstanceToValidate is not AuthorResource authorResource)
            {
                return true;
            }

            if (value.IsNullOrWhiteSpace())
            {
                return true;
            }

            var rootFolder = new DirectoryInfo(value!).Name;
            var author = authorResource.ToModel();
            var authorFolder = _fileNameBuilder.GetAuthorFolder(author);

            context.MessageFormatter.AppendArgument("rootFolderPath", value);
            context.MessageFormatter.AppendArgument("authorFolder", authorFolder);

            if (authorFolder == rootFolder)
            {
                return false;
            }

            var distance = authorFolder.LevenshteinDistance(rootFolder);

            return distance >= Math.Max(1, authorFolder.Length * 0.2);
        }
    }
}
