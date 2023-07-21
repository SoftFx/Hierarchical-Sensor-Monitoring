using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;

namespace HSMServer.Model.DataAlerts
{
    public sealed class TimeToLiveAlertViewModel : DataAlertViewModel
    {
        public const byte TimeToLiveAlertKey = byte.MaxValue;


        public TimeToLiveAlertViewModel(Guid entityId) : base(entityId) { }

        public TimeToLiveAlertViewModel(TTLPolicy policy, BaseNodeModel node) : base(policy, node) { }


        internal TimeToLiveAlertViewModel FromInterval(TimeIntervalViewModel interval)
        {
            Conditions.Clear();
            Conditions.Add(new TimeToLiveConditionViewModel()
            {
                Property = AlertProperty.TimeToLive,
                TimeToLive = new TimeIntervalViewModel(interval, PredefinedIntervals.ForTimeout) { IsAlertBlock = true },
            });

            return this;
        }

        protected override ConditionViewModel CreateCondition(bool isMain) => new TimeToLiveConditionViewModel();
    }
}
