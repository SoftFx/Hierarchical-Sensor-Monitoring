using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;
using System.Numerics;

namespace HSMServer.Model.DataAlerts
{
    public sealed class BarDataAlertViewModel<T, U> : DataAlertViewModel<T> where T : BarBaseValue<U>, new() where U : INumber<U>
    {
        public BarDataAlertViewModel(Guid entityId) : base(entityId) { }

        public BarDataAlertViewModel(Policy<T, U> policy, BaseSensorModel sensor) : base(policy, sensor) { }


        protected override ConditionViewModel CreateCondition(bool isMain) => new BarConditionViewModel<T, U>(isMain);
    }
}
