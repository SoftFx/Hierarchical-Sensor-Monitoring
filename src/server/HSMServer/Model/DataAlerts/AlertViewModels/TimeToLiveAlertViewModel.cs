using HSMCommon.Model;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.DataAlerts
{
    public sealed class TimeToLiveAlertViewModel : DataAlertViewModel
    {
        public const byte AlertKey = byte.MaxValue;

        public override SensorType Type => SensorType.Boolean;

        public override bool IsTtl => true;

        protected override string DefaultCommentTemplate { get; } = TTLPolicy.DefaultTemplate;

        protected override string DefaultIcon { get; } = TTLPolicy.DefaultIcon;

        public TimeToLiveAlertViewModel() : base()
        {
            FillConditions(new TimeIntervalViewModel(PredefinedIntervals.ForTimeout) { IsAlertBlock = true });
        }

        public TimeToLiveAlertViewModel(NodeViewModel node) : base(node)
        {
            if (node is null)
            {
                FillConditions(new TimeIntervalViewModel(PredefinedIntervals.ForTimeout) { IsAlertBlock = true });
                return;
            }

            FillConditions(new TimeIntervalViewModel(PredefinedIntervals.ForTimeout, () => (node.Parent?.TTL, node.ParentIsFolder)) { IsAlertBlock = true });
        }

        public TimeToLiveAlertViewModel(TTLPolicy policy, NodeViewModel node) : base(policy, node) { }

        public TimeToLiveAlertViewModel(TTLPolicy policy, TimeIntervalViewModel interval) : base(policy, null)
        {
            FillConditions(new TimeIntervalViewModel(PredefinedIntervals.ForTimeout, interval) { IsAlertBlock = true });
        }


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
