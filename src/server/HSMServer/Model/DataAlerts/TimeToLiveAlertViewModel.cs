using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.DataAlerts
{
    public sealed class TimeToLiveAlertViewModel : DataAlertViewModel
    {
        public const byte AlertKey = byte.MaxValue;


        public TimeToLiveAlertViewModel(NodeViewModel node) : base(node.Id)
        {
            FillConditions(new TimeIntervalViewModel(PredefinedIntervals.ForTimeout, () => (node.Parent?.TTL, node.ParentIsFolder)) { IsAlertBlock = true });
        }

        public TimeToLiveAlertViewModel(TTLPolicy policy, BaseNodeModel node) : base(policy, node) { }


        internal TimeToLiveAlertViewModel FromInterval(TimeIntervalViewModel interval)
        {
            FillConditions(new TimeIntervalViewModel(interval, PredefinedIntervals.ForTimeout) { IsAlertBlock = true });

            return this;
        }

        protected override ConditionViewModel CreateCondition(bool isMain) => new TimeToLiveConditionViewModel();

        private void FillConditions(TimeIntervalViewModel intervalBlock)
        {
            Conditions.Clear();
            Conditions.Add(new TimeToLiveConditionViewModel()
            {
                Property = AlertProperty.TimeToLive,
                TimeToLive = intervalBlock,
            });
        }
    }
}
