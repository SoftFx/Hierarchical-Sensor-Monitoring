using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Notifications.Telegram;
using System.Collections.Concurrent;
using System.Linq;
using Telegram.Bot.Types;

namespace HSMServer.Notification.Settings
{
    public enum InheritedSettings : byte
    {
        Custom,
        FromParent
    }

    public sealed class TelegramSettings
    {
        public ConcurrentDictionary<ChatId, TelegramChat> Chats { get; } = new();


        public SensorStatus MessagesMinStatus { get; private set; } = SensorStatus.Warning;

        public bool MessagesAreEnabled { get; private set; } = true;

        public int MessagesDelaySec { get; private set; } = 60;


        public InheritedSettings Inheritance { get; private set; } = InheritedSettings.Custom;


        public TelegramSettings() { }

        internal TelegramSettings(TelegramSettingsEntity entity)
        {
            if (entity == null)
                return;

            MessagesMinStatus = (SensorStatus)entity.MessagesMinStatus;
            MessagesAreEnabled = entity.MessagesAreEnabled;
            MessagesDelaySec = entity.MessagesDelay;
            Inheritance = (InheritedSettings)entity.Inheritance;

            if (entity.Chats != null)
                foreach (var chat in entity.Chats)
                    Chats.TryAdd(new(chat.Id), new TelegramChat(chat));
        }


        public void Update(TelegramMessagesSettingsUpdate settingsUpdate)
        {
            MessagesMinStatus = settingsUpdate.MinStatus ?? MessagesMinStatus;
            MessagesAreEnabled = settingsUpdate.Enabled ?? MessagesAreEnabled;
            MessagesDelaySec = settingsUpdate.Delay ?? MessagesDelaySec;
            Inheritance = settingsUpdate.Inheritance ?? Inheritance;
        }

        internal TelegramSettingsEntity ToEntity() =>
            new()
            {
                MessagesMinStatus = (byte)MessagesMinStatus,
                MessagesAreEnabled = MessagesAreEnabled,
                MessagesDelay = MessagesDelaySec,
                Inheritance = (byte)Inheritance,
                Chats = Chats.Select(ch => ch.Value.ToEntity()).ToList(),
            };
    }
}
