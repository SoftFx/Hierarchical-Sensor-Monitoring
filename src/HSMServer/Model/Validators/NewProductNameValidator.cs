using FluentValidation;
using HSMServer.Constants;
using HSMServer.MonitoringServerCore;
using System;
using System.Linq;

namespace HSMServer.Model.Validators
{
    public class NewProductNameValidator : AbstractValidator<string>
    {
        public readonly IMonitoringCore _monitoringCore;
        public NewProductNameValidator(IMonitoringCore monitoringCore)
        {
            _monitoringCore = monitoringCore;

            RuleFor(x => x)
                .NotNull()
                .WithMessage(ErrorConstants.NameNotNull)
                .Must(IsUniqueName)
                .WithMessage(ErrorConstants.NameUnique)
                .Matches(@"^[0-9a-zA-Z .,_\-=!#:;%&*()]+$")
                .WithMessage(ErrorConstants.ProductNameLatin);
        }

        private bool IsUniqueName(string name)
        {
            var products = _monitoringCore.GetAllProducts();

            return products?.FirstOrDefault(x =>
                x.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)) == null;
        }
    }
}
