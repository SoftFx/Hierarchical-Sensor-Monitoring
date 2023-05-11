using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class DoubleBarDataAlertViewModel : DataAlertViewModelBase<DoubleBarValue>
    {
        protected override List<string> Properties => new()
        {
            nameof(DoubleBarValue.Min),
            nameof(DoubleBarValue.Max),
            nameof(DoubleBarValue.Mean),
            nameof(DoubleBarValue.LastValue),
        };

        protected override List<Operation> Actions => new()
        {
            Operation.LessThanOrEqual,
            Operation.LessThan,
            Operation.GreaterThan,
            Operation.GreaterThanOrEqual,
        };


        public DoubleBarDataAlertViewModel() : base() { }

        public DoubleBarDataAlertViewModel(DoubleBarDataPolicy policy) : base(policy) { }
    }
}
