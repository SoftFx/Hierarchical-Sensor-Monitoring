using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class NotificationSettingsEntity
    {
        public TelegramSettingsEntity TelegramSettings { get; init; }

        public Dictionary<long, Dictionary<string, long>> PartiallyIgnored { get; init; }

        [Obsolete("Remove after migration IgnoredSensors->PartiallyIgnored")]
        public Dictionary<string, long> IgnoredSensors { get; init; }

        public List<string> EnabledSensors { get; init; }
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
        public long Id { get; init; }

        public string Name { get; init; }

        public bool IsUserChat { get; init; }

        public long AuthorizationTime { get; init; }
    }
}
