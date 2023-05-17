using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public abstract class BarDataAlertViewModel<T> : DataAlertViewModelBase<T> where T : BaseValue
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


        public BarDataAlertViewModel() : base() { }

        public BarDataAlertViewModel(DataPolicy<T> policy) : base(policy) { }
    }


    public class IntegerBarDataAlertViewModel : BarDataAlertViewModel<IntegerBarValue>
    {
        public IntegerBarDataAlertViewModel() : base() { }

        public IntegerBarDataAlertViewModel(IntegerBarDataPolicy policy) : base(policy) { }
    }


    public class DoubleBarDataAlertViewModel : BarDataAlertViewModel<DoubleBarValue>
    {
        public DoubleBarDataAlertViewModel() : base() { }

        public DoubleBarDataAlertViewModel(DoubleBarDataPolicy policy) : base(policy) { }
    }
}
