using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;

namespace HSMServer.Model.DataAlerts
{
    public sealed class TimeToLiveAlertViewModel : DataAlertViewModel
    {
        public TimeToLiveAlertViewModel(Guid entityId) : base(entityId) { }

        public TimeToLiveAlertViewModel(TimeIntervalViewModel interval, TTLPolicy policy, BaseNodeModel node)
            : base(policy, node)
        {
            Conditions.Add(new TimeToLiveConditionViewModel()
            {
                Property = AlertProperty.TimeToLive,
                TimeToLive = new TimeIntervalViewModel(interval, PredefinedIntervals.ForTimeout) { IsAlertBlock = true },
            });
        }


        protected override ConditionViewModel CreateCondition(bool isMain) => new TimeToLiveConditionViewModel();
    }
}
