using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public abstract class SimpleDataAlertViewModel<T> : DataAlertViewModelBase<T> where T : BaseValue
    {
        protected override List<string> Properties => new() { nameof(BaseValue<T>.Value) };

        protected override List<Operation> Actions => new()
        {
            Operation.LessThanOrEqual,
            Operation.LessThan,
            Operation.GreaterThan,
            Operation.GreaterThanOrEqual,
        };


        public SimpleDataAlertViewModel() : base() { }

        public SimpleDataAlertViewModel(DataPolicy<T> policy) : base(policy) { }
    }


    public sealed class IntegerDataAlertViewModel : SimpleDataAlertViewModel<IntegerValue>
    {
        public IntegerDataAlertViewModel() : base() { }

        public IntegerDataAlertViewModel(IntegerDataPolicy policy) : base(policy) { }
    }


    public sealed class DoubleDataAlertViewModel : SimpleDataAlertViewModel<DoubleValue>
    {
        public DoubleDataAlertViewModel() : base() { }

        public DoubleDataAlertViewModel(DoubleDataPolicy policy) : base(policy) { }
    }
}
