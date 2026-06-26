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

            // For a new TTL alert on a product/node, "From Parent" should reference this node's own
            // Settings.TTL — not the parent product/folder. For sensors, climb the parent chain.
            var (parent, isFolder) = node is ProductNodeViewModel
                ? (node.TTL, false)
                : (node.Parent?.TTL, node.ParentIsFolder);

            FillConditions(new TimeIntervalViewModel(PredefinedIntervals.ForTimeout, () => (parent, isFolder)) { IsAlertBlock = true });
        }

        public TimeToLiveAlertViewModel(TTLPolicy policy, NodeViewModel node) : base(policy, node)
        {
            TimeIntervalViewModel interval;

            if (policy.IsTTLFromParent && node != null)
            {
                if (node is ProductNodeViewModel)
                {
                    // Node-level TTL alert: "From Parent" resolves against this node's own Settings.TTL
                    // (bounded — does not climb to the parent product/folder). Build the parent view
                    // from the policy's resolved TTL so the UI matches the node's "Time to sensor(s) live" field.
                    var resolved = policy.TTLInterval.UseTicks
                        ? new TimeIntervalModel(policy.TTLInterval.Ticks)
                        : policy.TTLInterval;

                    var resolvedParent = new TimeIntervalViewModel(PredefinedIntervals.ForTimeout) { IsAlertBlock = true };
                    resolvedParent.FromModel(resolved, PredefinedIntervals.ForTimeout);

                    interval = new TimeIntervalViewModel(PredefinedIntervals.ForTimeout, () => (resolvedParent, false)) { IsAlertBlock = true };
                }
                else
                {
                    // Sensor-level TTL alert: keep the parent-chain resolution.
                    interval = new TimeIntervalViewModel(
                        PredefinedIntervals.ForTimeout,
                        () => (node.Parent?.TTL, node.ParentIsFolder)) { IsAlertBlock = true };
                }

                interval.Interval = TimeInterval.FromParent;
            }
            else
            {
                interval = new TimeIntervalViewModel(PredefinedIntervals.ForTimeout) { IsAlertBlock = true };
                interval.FromModel(policy.TTLInterval, PredefinedIntervals.ForTimeout);
            }

            FillConditions(interval);
        }

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
