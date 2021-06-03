using FluentValidation;
using HSMServer.Constants;
using HSMServer.DataLayer.Model;
using HSMServer.MonitoringServerCore;
using System;
using System.Linq;

namespace HSMServer.Model.Validators
{
    public class ProductValidator : AbstractValidator<Product>
    {
        public readonly IMonitoringCore _monitoringCore;
        public ProductValidator(IMonitoringCore monitoringCore)
        {
            _monitoringCore = monitoringCore;

            RuleFor(x => x.Name)
                .NotNull()
                .WithMessage(ErrorConstants.NameNotNull)
                .Must(name => IsUniqueName(name))
                .WithMessage(ErrorConstants.NameUnique);
        }

        private bool IsUniqueName(string name)
        {
            var products = _monitoringCore.GetAllProducts();

            return products?.FirstOrDefault(x =>
                x.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)) == null;
        }
    }
}
