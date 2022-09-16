using HSMDatabase.AccessManager.DatabaseEntities;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public sealed class TelegramSettings
    {
        private const int DefaultMinDelay = 10;
        private const bool DefaultEnableState = true;


        public SensorStatus MessagesMinStatus { get; private set; }

        public bool MessagesAreEnabled { get; private set; } = DefaultEnableState;

        public int MessagesDelay { get; private set; } = DefaultMinDelay;

        public List<TelegramChat> Chats { get; internal set; } = new();


        public TelegramSettings() { }

        internal TelegramSettings(TelegramSettingsEntity entity)
        {
            if (entity == null)
                return;

            MessagesMinStatus = (SensorStatus)entity.MessagesMinStatus;
            MessagesAreEnabled = entity.MessagesAreEnabled;
            MessagesDelay = entity.MessagesDelay;

            if (entity.Chats != null)
                foreach (var chat in entity.Chats)
                    Chats.Add(new TelegramChat(chat));

            if (entity.ChatIdentifier != 0 && !Chats.Any(ch => ch.Id.Identifier == entity.ChatIdentifier))
                Chats.Add(new TelegramChat() { Id = new(entity.ChatIdentifier), });
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
                Chats = new List<TelegramChatEntity>(Chats.Select(ch => ch.ToEntity())),
            };
    }
}
