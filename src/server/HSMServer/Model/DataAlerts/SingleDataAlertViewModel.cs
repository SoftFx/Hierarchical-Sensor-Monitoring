using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class SingleDataAlertViewModel<T, U> : DataAlertViewModelBase<T> where T : BaseValue<U>, new()
    {
        protected override List<string> Icons { get; } = AlertPredefined.BorderIcons;

        protected override List<string> Properties { get; } = new() { nameof(BaseValue<U>.Value) };

        protected override List<PolicyOperation> Actions { get; } = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
        };


        public SingleDataAlertViewModel(Guid entityId) : base(entityId) { }

        public SingleDataAlertViewModel(Policy<T, U> policy, BaseSensorModel sensor) : base(policy, sensor) { }
    }
}