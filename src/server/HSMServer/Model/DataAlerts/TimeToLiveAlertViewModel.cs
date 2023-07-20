using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;

namespace HSMServer.Model.DataAlerts
{
    public sealed class TimeToLiveAlertViewModel : DataAlertViewModel
    {
        public TimeToLiveAlertViewModel(Guid entityId) : base(entityId) { }

        public TimeToLiveAlertViewModel(TimeIntervalViewModel interval, TTLPolicy policy, BaseNodeModel node)
            : base(policy, node, policy.Template)
        {
            Conditions.Add(new TimeToLiveConditionViewModel()
            {
                Property = AlertProperty.TimeToLive,
                TimeToLive = interval,
            });
        }


        protected override ConditionViewModel CreateCondition(bool isMain) => new TimeToLiveConditionViewModel();
    }
}
