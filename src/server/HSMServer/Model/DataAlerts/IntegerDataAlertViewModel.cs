using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class IntegerDataAlertViewModel : DataAlertViewModelBase
    {
        protected override List<string> Properties => new() { "Value" };

        protected override List<Operation> Actions => new()
        {
            Operation.LessThanOrEqual,
            Operation.LessThan,
            Operation.GreatedThan,
            Operation.GreaterThanOrEqual,
        };


        public IntegerDataAlertViewModel() { }
    }
}
