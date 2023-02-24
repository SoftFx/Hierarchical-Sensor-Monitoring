using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using System.Collections.Generic;
using HSMServer.Core.Model;

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

        public override bool IsIgnoredState { get; set; } = true;

        internal NodeShallowModel(ProductNodeViewModel data, User user) : base(data, user)
        {
        }


        internal void AddChild(SensorShallowModel shallowSensor, User user)
        {
            shallowSensor.Parent = this;
            
            var sensor = shallowSensor.Data;

            if (sensor.State != SensorState.Ignored)
            {
                AccountState.CalculateState(user.Notifications, sensor.Id);
                GroupState.CalculateState(sensor.RootProduct.Notifications, sensor.Id);
            }
            
            IsIgnoredState &= sensor.State == SensorState.Ignored;

            if (user.IsSensorVisible(sensor))
            {
                VisibleSensorsCount++;
                Sensors.Add(shallowSensor);
            }
        }

        internal void AddChild(NodeShallowModel node, User user)
        {
            node.Parent = this;
            
            if (!node.IsIgnoredState)
            {
                AccountState.CalculateState(node.AccountState);
                GroupState.CalculateState(node.GroupState);
            }
            
            IsIgnoredState &= node.IsIgnoredState;

            VisibleSensorsCount += node.VisibleSensorsCount;

            if (node.VisibleSensorsCount > 0 || user.IsEmptyProductVisible(node.Data))
                Nodes.Add(node);
        }
    }
}
