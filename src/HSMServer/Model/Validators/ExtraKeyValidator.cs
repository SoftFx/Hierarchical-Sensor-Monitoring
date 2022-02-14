using FluentValidation;
using HSMServer.Constants;
using HSMServer.Core.Products;
using HSMServer.Model.ViewModel;
using System.Linq;

namespace HSMServer.Model.Validators
{
    public class ExtraKeyValidator : AbstractValidator<ExtraKeyViewModel>
    {
        private readonly IProductManager _productManager;
        public ExtraKeyValidator(IProductManager productManager)
        {
            _productManager = productManager;

            RuleFor(x => x.ExtraKeyName)
                .NotNull()
                .NotEmpty()
                .WithMessage(ErrorConstants.ExtraKeyNameNotNull);

            When(x => x.ExtraKeyName != null, () =>
            {
                RuleFor(x => x)
                    .Must(x => IsUniqueName(x.ExtraKeyName, x.ProductKey))
                    .WithMessage(ErrorConstants.ExtraKeyNameUnique);
            });
        }

        private bool IsUniqueName(string extraKeyName, string productKey)
        {
            var product = _productManager.GetProductByKey(productKey);

            return product?.ExtraKeys?.FirstOrDefault(x
                => x.Name.Equals(extraKeyName,
                System.StringComparison.InvariantCultureIgnoreCase)) == null;
        }
    }
}
