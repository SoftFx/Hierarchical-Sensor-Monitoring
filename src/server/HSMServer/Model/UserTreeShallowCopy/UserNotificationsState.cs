using HSMServer.Core.Model;
using System;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class UserNotificationsState
    {
        public bool IsAnyEnabled { get; private set; }

        public bool IsAllEnabled { get; private set; } = true;

        public bool IsAllIgnored { get; private set; } = true;

        public void CalculateState(NotificationSettings settings, Guid sensorId)
        {
            ChangeEnableState(settings.IsSensorEnabled(sensorId));
            ChangeIgnoreState(settings.IsSensorIgnored(sensorId));
        }

        public void CalculateState(UserNotificationsState state)
        {
            ChangeEnableState(state.IsAnyEnabled);
            ChangeIgnoreState(state.IsAllIgnored);
        }

        private void ChangeEnableState(bool isEnabled)
        {
            IsAnyEnabled |= isEnabled;
            IsAllEnabled &= isEnabled;
        }

        private void ChangeIgnoreState(bool isIgnored) =>
            IsAllIgnored &= isIgnored;
    }
}
