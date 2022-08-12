﻿using HSMDatabase.AccessManager.DatabaseEntities;
using Telegram.Bot.Types;

namespace HSMServer.Core.Model
{
    public sealed class TelegramSettings
    {
        private const int DefaultMinDelay = 10;
        private const bool DefaultEnableState = true;


        public SensorStatus MessagesMinStatus { get; private set; }

        public bool MessagesAreEnabled { get; private set; } = DefaultEnableState;

        public int MessagesDelay { get; private set; } = DefaultMinDelay;

        public ChatId Chat { get; internal set; }


        public TelegramSettings() { }

        internal TelegramSettings(TelegramSettingsEntity entity)
        {
            if (entity == null)
                return;

            MessagesMinStatus = (SensorStatus)entity.MessagesMinStatus;
            MessagesAreEnabled = entity.MessagesAreEnabled;
            MessagesDelay = entity.MessagesDelay;

            if (entity.ChatIdentifier != 0)
                Chat = new(entity.ChatIdentifier);
        }


        public void Update(TelegramMessagesSettingsUpdate settingsUpdate)
        {
            MessagesMinStatus = settingsUpdate.MinStatus;
            MessagesAreEnabled = settingsUpdate.Enabled;
            MessagesDelay = settingsUpdate.Delay;
        }

        internal TelegramSettingsEntity ToEntity() =>
            new()
            {
                MessagesMinStatus = (byte)MessagesMinStatus,
                MessagesAreEnabled = MessagesAreEnabled,
                MessagesDelay = MessagesDelay,
                ChatIdentifier = Chat?.Identifier ?? 0,
            };
    }
}
