using HSMDatabase.AccessManager.DatabaseEntities;
using Telegram.Bot.Types;

namespace HSMServer.Core.Model
{
    public sealed class TelegramSettings
    {
        private const int DefaultMinDelay = 10;
        private const bool DefaultEnableState = true;


        public SensorStatus TelegramMessagesMinStatus { get; init; }

        public bool EnableTelegramMessages { get; init; } = DefaultEnableState;

        public int TelegramMessagesDelay { get; init; } = DefaultMinDelay;

        public ChatId Chat { get; internal set; }


        public TelegramSettings() { }

        internal TelegramSettings(TelegramSettingsEntity entity)
        {
            if (entity == null)
                return;

            TelegramMessagesMinStatus = (SensorStatus)entity.TelegramMessagesMinStatus;
            EnableTelegramMessages = entity.EnableTelegramMessages;
            TelegramMessagesDelay = entity.TelegramMessagesDelay;

            if (entity.ChatIdentifier.HasValue)
                Chat = new(entity.ChatIdentifier.Value);
        }


        internal TelegramSettingsEntity ToEntity() =>
            new()
            {
                TelegramMessagesMinStatus = (byte)TelegramMessagesMinStatus,
                EnableTelegramMessages = EnableTelegramMessages,
                TelegramMessagesDelay = TelegramMessagesDelay,
                ChatIdentifier = Chat?.Identifier,
            };
    }
}
