using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModels
{
    public sealed class TreeNodeStateViewModel
    {
        public List<TreeNodeStateViewModel> Nodes { get; } = new(1 << 4);

        public List<SensorNodeViewModel> Sensors { get; } = new(1 << 4);

        public ProductNodeViewModel Data { get; }
        

        public bool IsAnyAccountsNotificationsEnabled { get; private set; }

        public bool IsAllAccountsNotificationsEnabled { get; private set; } = true;

        public bool IsAllAccountsNotificationsIgnored { get; private set; } = true;
        
        public bool IsAnyGroupsNotificationsEnabled { get; private set; }
        
        public bool IsAllGroupsNotificationsEnabled { get; private set; } = true;
        
        public bool IsAllGroupsNotificationsIgnored { get; private set; } = true;
        
        
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
            
            ChangeGroupsEnableState(sensor.GroupNotifications.IsSensorEnabled(sensor.Id));
            ChangeGroupsIgnoreState(sensor.GroupNotifications.IsSensorIgnored(sensor.Id));
            
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
            
            ChangeGroupsEnableState(node.IsAnyGroupsNotificationsEnabled);
            ChangeGroupsIgnoreState(node.IsAllGroupsNotificationsIgnored);
            
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
        
        private void ChangeGroupsEnableState(bool isEnabled)
        {
            IsAnyGroupsNotificationsEnabled |= isEnabled;
            IsAllGroupsNotificationsEnabled &= isEnabled;
        }

        private void ChangeGroupsIgnoreState(bool isIgnored) =>
            IsAllGroupsNotificationsIgnored &= isIgnored;
    }
}
