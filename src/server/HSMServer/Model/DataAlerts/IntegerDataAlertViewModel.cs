using HSMServer.Core.Model;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class IntegerDataAlertViewModel : DataAlertViewModelBase
    {
        protected override List<string> Properties => new() { nameof(IntegerValue.Value) };

        protected override List<Operation> Actions => new()
        {
            Operation.LessThanOrEqual,
            Operation.LessThan,
            Operation.GreaterThan,
            Operation.GreaterThanOrEqual,
        };


        public IntegerDataAlertViewModel() : base() { }
    }
}
