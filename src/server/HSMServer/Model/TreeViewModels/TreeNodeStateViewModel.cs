using HSMServer.Core.Model;
using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModels
{
    public sealed class TreeNodeStateViewModel
    {
        public List<TreeNodeStateViewModel> Nodes { get; } = new(1 << 4);

        public List<TreeSensorViewModel> Sensors { get; } = new(1 << 4);

        public TreeProductViewModel Data { get; init; }

        public bool IsAnyNotificationsEnabled { get; private set; }

        public bool IsAllNotificationsEnabled { get; private set; }

        public bool IsAllNotificationsIgnored { get; private set; }

        public int FilteredSensorsCount { get; private set; }


        public TreeNodeStateViewModel() => Reset();


        public string GetSensorsCountString()
        {
            var sensorsCount = FilteredSensorsCount == Data.AllSensorsCount
                ? $"{Data.AllSensorsCount}"
                : $"{FilteredSensorsCount}/{Data.AllSensorsCount}";

            return $"({sensorsCount} sensors)";
        }


        public void ChangeEnableState(bool isNotificationsEnabled)
        {
            IsAnyNotificationsEnabled |= isNotificationsEnabled;
            IsAllNotificationsEnabled &= isNotificationsEnabled;
        }

        public void ChangeIgnoreState(bool isNotificationsIgnored) =>
            IsAllNotificationsIgnored &= isNotificationsIgnored;

        public void ChangeSensorsCount(int visibleSensors) =>
            FilteredSensorsCount += visibleSensors;

        public void AddChildState(TreeNodeStateViewModel childState)
        {
            ChangeSensorsCount(childState.FilteredSensorsCount);
            ChangeEnableState(childState.IsAnyNotificationsEnabled);
            ChangeIgnoreState(childState.IsAllNotificationsIgnored);
        }

        internal void Reset()
        {
            IsAnyNotificationsEnabled = false;
            IsAllNotificationsEnabled = true;
            IsAllNotificationsIgnored = true;

            FilteredSensorsCount = 0;
        }
    }
}
