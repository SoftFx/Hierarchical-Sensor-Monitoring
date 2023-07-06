using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Policies.Infrastructure;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class BarDataAlertViewModel<T, U> : DataAlertViewModelBase<T> where T : BarBaseValue<U>, new() where U : struct
    {
        public override string DisplayComment { get; }


        protected override List<string> Icons { get; } = AlertPredefined.BorderIcons;

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


        public BarDataAlertViewModel(Guid entityId) : base(entityId)
        {
            Conditions.Add(new BarConditionViewModel<T, U>(true));
        }

        public BarDataAlertViewModel(Policy<T, U> policy, BaseSensorModel sensor) : base(policy, sensor)
        {
            DisplayComment = CommentBuilder.GetBarComment(sensor.LastValue as T, sensor, policy);
        }
    }
}
