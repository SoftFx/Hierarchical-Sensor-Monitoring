using FluentValidation;
using HSMServer.Constants;
using HSMServer.Core.Products;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace HSMServer.Model.Validators
{
    public class NewProductNameValidator : AbstractValidator<string>
    {
        private readonly IProductManager _productManager;
        public NewProductNameValidator(IProductManager productManager)
        {
            _productManager = productManager;

            RuleFor(x => x)
                .NotNull()
                .WithMessage(ErrorConstants.NameNotNull)
                .Must(IsUniqueName)
                .WithMessage(ErrorConstants.NameUnique)
                .Matches(@"^[0-9a-zA-Z .,_\-=#:;%&*()]*$", RegexOptions.IgnoreCase)
                .WithMessage(ErrorConstants.ProductNameSymbols);
        }

        private bool IsUniqueName(string name)
        {
            var products = _productManager.Products;

            return products?.FirstOrDefault(x =>
                x.DisplayName.Equals(name, StringComparison.InvariantCultureIgnoreCase)) == null;
        }
    }
}
