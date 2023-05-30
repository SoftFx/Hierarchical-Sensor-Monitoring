﻿using HSMServer.Notification.Settings;
using System;
using Telegram.Bot.Types;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class UserNotificationsState
    {
        public ChatId ChatId { get; }

        public bool IsAllEnabled { get; private set; } = true;

        public bool IsAllIgnored { get; private set; } = true;


        public void CalculateState(ClientNotifications settings, Guid sensorId)
        {
            ChangeEnableState(settings.IsSensorEnabled(sensorId));
            ChangeIgnoreState(settings.IsSensorIgnored(sensorId)); // TODO: check for chat
        }

        public void CalculateState(UserNotificationsState state)
        {
            ChangeEnableState(state.IsAllEnabled);
            ChangeIgnoreState(state.IsAllIgnored);
        }

        private void ChangeEnableState(bool isEnabled) =>
            IsAllEnabled &= isEnabled;

        private void ChangeIgnoreState(bool isIgnored) =>
            IsAllIgnored &= isIgnored;
    }
}
