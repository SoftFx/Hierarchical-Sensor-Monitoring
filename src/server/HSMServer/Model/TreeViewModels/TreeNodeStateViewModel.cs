using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModels
{
    public sealed class NotificationsState
    {
        public bool IsAnyEnabled { get; private set; }

        public bool IsAllEnabled { get; private set; }

        public bool IsAllIgnored { get; private set; }

        public void CalculateState(NotificationSettings settings, Guid sensorId)
        {
            ChangeEnableState(settings.IsSensorEnabled(sensorId));
            ChangeIgnoreState(settings.IsSensorIgnored(sensorId));
        }

        public void CalculateState(TreeNodeStateViewModel node)
        {
            ChangeEnableState(node.NotificationsState.IsAnyEnabled);
            ChangeIgnoreState(node.NotificationsState.IsAllIgnored);
        }

        private void ChangeEnableState(bool isEnabled)
        {
            IsAnyEnabled |= isEnabled;
            IsAllEnabled &= isEnabled;
        }

        private void ChangeIgnoreState(bool isIgnored) =>
            IsAllIgnored &= isIgnored;
    }


    public sealed class TreeNodeStateViewModel
    {
        public List<TreeNodeStateViewModel> Nodes { get; } = new(1 << 4);

        public List<SensorNodeViewModel> Sensors { get; } = new(1 << 4);

        public ProductNodeViewModel Data { get; }

        public NotificationsState NotificationsState { get; set; } = new();

        public bool IsAnyAccountsNotificationsEnabled { get; private set; }

        public bool IsAllAccountsNotificationsEnabled { get; private set; } = true;

        public bool IsAllAccountsNotificationsIgnored { get; private set; } = true;

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


        internal TreeNodeStateViewModel(ProductNodeViewModel data)
        {
            Data = data;
        }


        internal void AddChild(SensorNodeViewModel sensor, User user)
        {
            ChangeAccountsEnableState(user.Notifications.IsSensorEnabled(sensor.Id));
            ChangeAccountsIgnoreState(user.Notifications.IsSensorIgnored(sensor.Id));

            NotificationsState.CalculateState(sensor.GroupNotifications, sensor.Id);

            if (user.IsSensorVisible(sensor))
            {
                VisibleSensorsCount++;
                Sensors.Add(sensor);
            }
        }

        internal void AddChild(TreeNodeStateViewModel node, User user)
        {
            ChangeAccountsEnableState(node.IsAnyAccountsNotificationsEnabled);
            ChangeAccountsIgnoreState(node.IsAllAccountsNotificationsIgnored);

            NotificationsState.CalculateState(node);

            VisibleSensorsCount += node.VisibleSensorsCount;

            if (node.VisibleSensorsCount > 0 || user.IsEmptyProductVisible(node.Data))
                Nodes.Add(node);
        }

        private void ChangeAccountsEnableState(bool isEnabled)
        {
            IsAnyAccountsNotificationsEnabled |= isEnabled;
            IsAllAccountsNotificationsEnabled &= isEnabled;
        }

        private void ChangeAccountsIgnoreState(bool isIgnored) =>
            IsAllAccountsNotificationsIgnored &= isIgnored;
    }
}
