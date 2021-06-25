using FluentValidation;
using HSMServer.Constants;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using System.Linq;

namespace HSMServer.Model.Validators
{
    public class ExtraKeyValidator : AbstractValidator<ExtraKeyViewModel>
    {
        public readonly IMonitoringCore _monitoringCore;
        public ExtraKeyValidator(IMonitoringCore monitoringCore)
        {
            _monitoringCore = monitoringCore;

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
            var product = _monitoringCore.GetProduct(productKey);

            return product?.ExtraKeys?.FirstOrDefault(x
                => x.Name.Equals(extraKeyName,
                System.StringComparison.InvariantCultureIgnoreCase)) == null;
        }
    }
}
