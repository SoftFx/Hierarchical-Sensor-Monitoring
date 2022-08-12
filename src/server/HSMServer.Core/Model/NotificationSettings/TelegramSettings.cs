using HSMDatabase.AccessManager.DatabaseEntities;
using Telegram.Bot.Types;

namespace HSMServer.Core.Model
{
    public sealed class TelegramSettings
    {
        private const int DefaultMinDelay = 10;
        private const bool DefaultEnableState = true;


        public SensorStatus MessagesMinStatus { get; init; }

        public bool MessagesAreEnabled { get; init; } = DefaultEnableState;

        public int MessagesDelay { get; init; } = DefaultMinDelay;

        public ChatId Chat { get; internal set; }


        public TelegramSettings() { }

        internal TelegramSettings(TelegramSettingsEntity entity)
        {
            if (entity == null)
                return;

            MessagesMinStatus = (SensorStatus)entity.MessagesMinStatus;
            MessagesAreEnabled = entity.MessagesAreEnabled;
            MessagesDelay = entity.MessagesDelay;

            if (entity.ChatIdentifier.HasValue)
                Chat = new(entity.ChatIdentifier.Value);
        }


        internal TelegramSettingsEntity ToEntity() =>
            new()
            {
                MessagesMinStatus = (byte)MessagesMinStatus,
                MessagesAreEnabled = MessagesAreEnabled,
                MessagesDelay = MessagesDelay,
                ChatIdentifier = Chat?.Identifier,
            };
    }
}
