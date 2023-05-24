using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Policies.Infrastructure;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public abstract class SimpleDataAlertViewModel<T, U> : DataAlertViewModelBase<T> where T : BaseValue<U>
    {
        protected override List<string> Properties { get; } = new() { nameof(BaseValue<U>.Value) };

        protected override List<PolicyOperation> Actions { get; } = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
        };


        public SimpleDataAlertViewModel() : base() { }

        public SimpleDataAlertViewModel(DataPolicy<T, U> policy, BaseSensorModel sensor) : base(policy)
        {
            DisplayComment = CommentBuilder.GetSingleComment(sensor.LastValue as T, sensor, policy);
        }
    }


    public sealed class IntegerDataAlertViewModel : SimpleDataAlertViewModel<IntegerValue, int>
    {
        public IntegerDataAlertViewModel() : base() { }

        public IntegerDataAlertViewModel(IntegerDataPolicy policy, BaseSensorModel sensor) : base(policy, sensor) { }
    }


    public sealed class DoubleDataAlertViewModel : SimpleDataAlertViewModel<DoubleValue, double>
    {
        public DoubleDataAlertViewModel() : base() { }

        public DoubleDataAlertViewModel(DoubleDataPolicy policy, BaseSensorModel sensor) : base(policy, sensor) { }
    }
}
