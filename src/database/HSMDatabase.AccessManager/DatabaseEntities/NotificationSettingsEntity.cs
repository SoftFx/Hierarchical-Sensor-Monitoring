using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class NotificationSettingsEntity
    {
        public TelegramSettingsEntity TelegramSettings { get; init; }

        public Dictionary<long, Dictionary<string, long>> PartiallyIgnored { get; init; }

        public List<string> EnabledSensors { get; init; }

        public bool AutoSubscription { get; init; } = true;
    }


    public sealed class TelegramSettingsEntity
    {
        public List<TelegramChatEntity> Chats { get; init; }

        public byte MessagesMinStatus { get; init; }

        public bool MessagesAreEnabled { get; init; }

        public int MessagesDelay { get; init; }

        public byte Inheritance { get; init; }
    }


    public sealed class TelegramChatEntity
    {
        public byte[] SystemId { get; init; }

        public long Id { get; init; }


        public string Name { get; init; }

        public bool IsUserChat { get; init; }

        public long AuthorizationTime { get; init; }
    }
}
