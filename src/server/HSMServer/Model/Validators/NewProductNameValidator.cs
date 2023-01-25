using FluentValidation;
using FluentValidation.Results;
using HSMServer.Constants;
using HSMServer.Core.Cache;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace HSMServer.Model.Validators
{
    public class NewProductNameValidator : AbstractValidator<string>
    {
        private readonly ITreeValuesCache _cache;


        public NewProductNameValidator(ITreeValuesCache cache)
        {
            _cache = cache;

            RuleFor(x => x)
                .NotNull()
                .WithMessage(ErrorConstants.NameNotNull)
                .MaximumLength(100)
                .WithMessage(ErrorConstants.ProductNameMaxLength)
                .Must(IsUniqueName)
                .WithMessage(ErrorConstants.NameUnique)
                .Matches(@"^[0-9a-zA-Z .,_\-=#:;%&*()]*$", RegexOptions.IgnoreCase)
                .WithMessage(ErrorConstants.ProductNameSymbols);
        }


        // TODO: Remove IsUniqName validation after fixing saving products in db (ProductName to Id)
        private bool IsUniqueName(string name)
        {
            var products = _cache.GetProducts();

            return products?.FirstOrDefault(x =>
                x.DisplayName.Equals(name, StringComparison.InvariantCultureIgnoreCase)) == null;
        }

        protected override bool PreValidate(ValidationContext<string> context, ValidationResult result)
        {
            if (context.InstanceToValidate == null)
            {
                result.Errors.Add(new ValidationFailure("ProductName", "Product name must be not null"));
                return false;
            }
            return true;
        }
    }
}
