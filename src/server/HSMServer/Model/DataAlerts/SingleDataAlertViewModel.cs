using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Policies.Infrastructure;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class SingleDataAlertViewModel<T, U> : DataAlertViewModelBase<T> where T : BaseValue<U>, new()
    {
        public override string DisplayComment { get; }

        protected override List<string> Properties { get; } = new() { nameof(BaseValue<U>.Value) };

        protected override List<PolicyOperation> Actions { get; } = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
        };


        public SingleDataAlertViewModel(Guid entityId) : base(entityId) { }

        public SingleDataAlertViewModel(DataPolicy<T, U> policy, BaseSensorModel sensor) : base(policy, sensor)
        {
            DisplayComment = CommentBuilder.GetSingleComment(sensor.LastValue as T, sensor, policy);
        }
    }
}
