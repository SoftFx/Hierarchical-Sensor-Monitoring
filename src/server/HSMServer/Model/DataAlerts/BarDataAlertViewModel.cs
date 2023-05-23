using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Policies.Infrastructure;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public abstract class BarDataAlertViewModel<T, U> : DataAlertViewModelBase<T> where T : BarBaseValue<U> where U : struct
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

        public BarDataAlertViewModel(DataPolicy<T, U> policy, BaseSensorModel sensor) : base(policy)
        {
            DisplayComment = CustomCommentBuilder.GetBarComment(sensor.LastValue as T, sensor, policy);
        }
    }


    public class IntegerBarDataAlertViewModel : BarDataAlertViewModel<IntegerBarValue, int>
    {
        public IntegerBarDataAlertViewModel() : base() { }

        public IntegerBarDataAlertViewModel(IntegerBarDataPolicy policy, BaseSensorModel sensor) : base(policy, sensor) { }
    }


    public class DoubleBarDataAlertViewModel : BarDataAlertViewModel<DoubleBarValue, double>
    {
        public DoubleBarDataAlertViewModel() : base() { }

        public DoubleBarDataAlertViewModel(DoubleBarDataPolicy policy, BaseSensorModel sensor) : base(policy, sensor) { }
    }
}
