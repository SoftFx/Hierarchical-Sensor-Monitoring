using HSMServer.Core.Model.Authentication;
using HSMServer.Extensions;
using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModels
{
    public sealed class TreeNodeStateViewModel
    {
        public List<TreeNodeStateViewModel> Nodes { get; } = new(1 << 4);

        public List<SensorNodeViewModel> Sensors { get; } = new(1 << 4);

        public ProductNodeViewModel Data { get; }

        public bool IsAnyNotificationsEnabled { get; private set; }

        public bool IsAllNotificationsEnabled { get; private set; } = true;

        public bool IsAllNotificationsIgnored { get; private set; } = true;

        public int FilteredSensorsCount { get; private set; }

        public string SensorsCountString
        {
            get
            {
                var sensorsCount = FilteredSensorsCount == Data.AllSensorsCount
                    ? $"{Data.AllSensorsCount}"
                    : $"{FilteredSensorsCount}/{Data.AllSensorsCount}";

                return $"({sensorsCount} sensors)";
            }
        }


        internal TreeNodeStateViewModel(ProductNodeViewModel data)
        {
            Data = data;
        }


        internal void AddSensorState(User user, SensorNodeViewModel sensor)
        {
            var isSensorVisible = user.IsSensorVisible(sensor);

            ChangeSensorsCount(isSensorVisible ? 1 : 0);
            ChangeEnableState(user.Notifications.IsSensorEnabled(sensor.Id));
            ChangeIgnoreState(user.Notifications.IsSensorIgnored(sensor.Id));

            if (isSensorVisible)
                Sensors.Add(sensor);
        }

        internal void AddChildState(TreeNodeStateViewModel childState)
        {
            ChangeSensorsCount(childState.FilteredSensorsCount);
            ChangeEnableState(childState.IsAnyNotificationsEnabled);
            ChangeIgnoreState(childState.IsAllNotificationsIgnored);
        }

        private void ChangeEnableState(bool isNotificationsEnabled)
        {
            IsAnyNotificationsEnabled |= isNotificationsEnabled;
            IsAllNotificationsEnabled &= isNotificationsEnabled;
        }

        private void ChangeIgnoreState(bool isNotificationsIgnored) =>
            IsAllNotificationsIgnored &= isNotificationsIgnored;

        private void ChangeSensorsCount(int visibleSensors) =>
            FilteredSensorsCount += visibleSensors;
    }
}
