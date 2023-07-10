using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;

namespace HSMServer.Model.DataAlerts
{
    public sealed class SingleDataAlertViewModel<T, U> : DataAlertViewModelBase<T> where T : BaseValue<U>, new()
    {
        public SingleDataAlertViewModel(Guid entityId) : base(entityId)
        {
            Conditions.Add(new SingleConditionViewModel<T, U>(true));
        }

        public SingleDataAlertViewModel(Policy<T, U> policy, BaseSensorModel sensor) : base(policy, sensor) { }


        protected override ConditionViewModel CreateCondition(bool isFirst) => new SingleConditionViewModel<T, U>(isFirst);
    }
}