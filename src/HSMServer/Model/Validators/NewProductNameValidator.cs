using FluentValidation;
using HSMServer.Constants;
using HSMServer.Core.MonitoringServerCore;
using System;
using System.Linq;
using System.Text.RegularExpressions;

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
                .Matches(@"^[0-9a-zA-Z .,_\-=#:;%&*()]*$", RegexOptions.IgnoreCase)
                .WithMessage(ErrorConstants.ProductNameSymbols);
        }

        private bool IsUniqueName(string name)
        {
            var products = _monitoringCore.GetAllProducts();

            return products?.FirstOrDefault(x =>
                x.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)) == null;
        }
    }
}
