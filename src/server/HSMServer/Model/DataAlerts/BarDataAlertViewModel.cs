using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Policies.Infrastructure;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public abstract class BarDataAlertViewModel<T, U> : DataAlertViewModelBase<T> where T : BarBaseValue<U> where U : struct
    {
        protected override List<string> Properties { get; } = new()
        {
            nameof(BarBaseValue<U>.Min),
            nameof(BarBaseValue<U>.Max),
            nameof(BarBaseValue<U>.Mean),
            nameof(BarBaseValue<U>.LastValue),
        };

        protected override List<PolicyOperation> Actions { get; } = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
        };


        public BarDataAlertViewModel() : base() { }

        public BarDataAlertViewModel(DataPolicy<T, U> policy, BaseSensorModel sensor) : base(policy)
        {
            DisplayComment = CommentBuilder.GetBarComment(sensor.LastValue as T, sensor, policy);
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
