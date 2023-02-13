using HSMDatabase.AccessManager.DatabaseEntities;
using System.Collections.Concurrent;
using System.Linq;
using Telegram.Bot.Types;

namespace HSMServer.Core.Model
{
    public sealed class TelegramSettings
    {
        private const int DefaultMinDelay = 10;
        private const bool DefaultEnableState = true;


        public SensorStatus MessagesMinStatus { get; private set; } = SensorStatus.Warning;

        public bool MessagesAreEnabled { get; private set; } = DefaultEnableState;

        public int MessagesDelay { get; private set; } = DefaultMinDelay;

        public ConcurrentDictionary<ChatId, TelegramChat> Chats { get; } = new();


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
                    Chats.TryAdd(new(chat.Id), new TelegramChat(chat));

            if (entity.ChatIdentifier != 0) //TODO: migration logic should be removed
            {
                var chatId = new ChatId(entity.ChatIdentifier);
                Chats.TryAdd(chatId, new TelegramChat()
                {
                    Id = chatId,
                    Name = string.Empty,
                    IsUserChat = true,
                });
            }
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
                Chats = Chats.Select(ch => ch.Value.ToEntity()).ToList(),
            };
    }
}
