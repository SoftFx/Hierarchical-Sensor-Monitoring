using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class TelegramSettings
    {
        public SensorStatus TelegramMessagesMinStatus { get; init; }

        public bool EnableTelegramMessages { get; init; } = CommonConstants.DefaultTelegramMessagesEnableState;

        public int TelegramMessagesDelay { get; init; } = CommonConstants.DefaultTelegramMessagesMinDelay;


        public TelegramSettings() { }

        internal TelegramSettings(TelegramSettings settings)
        {
            if (settings == null)
                return;

            TelegramMessagesMinStatus = settings.TelegramMessagesMinStatus;
            EnableTelegramMessages = settings.EnableTelegramMessages;
            TelegramMessagesDelay = settings.TelegramMessagesDelay;
        }

        internal TelegramSettings(TelegramSettingsEntity entity)
        {
            if (entity == null)
                return;

            TelegramMessagesMinStatus = (SensorStatus)entity.TelegramMessagesMinStatus;
            EnableTelegramMessages = entity.EnableTelegramMessages;
            TelegramMessagesDelay = entity.TelegramMessagesDelay;
        }


        internal TelegramSettingsEntity ToEntity() =>
            new()
            {
                TelegramMessagesMinStatus = (byte)TelegramMessagesMinStatus,
                EnableTelegramMessages = EnableTelegramMessages,
                TelegramMessagesDelay = TelegramMessagesDelay,
            };
    }
}
