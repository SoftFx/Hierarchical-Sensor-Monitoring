using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using System.Collections.Generic;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class NodeShallowModel : BaseShallowModel<ProductNodeViewModel>
    {
        public List<NodeShallowModel> Nodes { get; } = new(1 << 4);

        public List<SensorShallowModel> Sensors { get; } = new(1 << 4);

        public UserNotificationsState GroupState { get; } = new();

        public UserNotificationsState AccountState { get; } = new();


        public int VisibleSensorsCount { get; private set; }

        public string SensorsCountString
        {
            get
            {
                var sensorsCount = VisibleSensorsCount == Data.AllSensorsCount
                    ? $"{Data.AllSensorsCount}"
                    : $"{VisibleSensorsCount}/{Data.AllSensorsCount}";

                return $"({sensorsCount} sensors)";
            }
        }

        public override bool IsAccountsEnable => AccountState.IsAllEnabled;

        public override bool IsGroupsEnable => GroupState.IsAllEnabled;

        public override bool IsIgnoredState { get; }

        internal NodeShallowModel(ProductNodeViewModel data, User user) : base(data, user)
        {
        }


        internal void AddChild(SensorShallowModel shallowSensor, User user)
        {
            var sensor = shallowSensor.Data;

            AccountState.CalculateState(user.Notifications, sensor.Id);
            GroupState.CalculateState(sensor.GroupNotifications, sensor.Id);

            if (user.IsSensorVisible(sensor))
            {
                VisibleSensorsCount++;
                Sensors.Add(shallowSensor);
            }
        }

        internal void AddChild(NodeShallowModel node, User user)
        {
            AccountState.CalculateState(node.AccountState);
            GroupState.CalculateState(node.GroupState);

            VisibleSensorsCount += node.VisibleSensorsCount;

            if (node.VisibleSensorsCount > 0 || user.IsEmptyProductVisible(node.Data))
                Nodes.Add(node);
        }
    }
}
