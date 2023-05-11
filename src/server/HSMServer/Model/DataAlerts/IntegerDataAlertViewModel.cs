using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class IntegerDataAlertViewModel : DataAlertViewModelBase<IntegerValue>
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

        public IntegerDataAlertViewModel(IntegerDataPolicy policy) : base(policy) { }
    }
}
