using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed class NotificationSettingsEntity
    {
        public TelegramSettingsEntity TelegramSettings { get; init; }

        public List<string> EnabledSensors { get; init; }

        public Dictionary<string, long> IgnoredSensors { get; init; }
    }


    public sealed class TelegramSettingsEntity
    {
        public byte MessagesMinStatus { get; init; }

        public bool MessagesAreEnabled { get; init; }

        public int MessagesDelay { get; init; }

        public List<TelegramChatEntity> Chats { get; init; }

        [Obsolete]
        public long ChatIdentifier { get; init; }
    }


    public sealed class TelegramChatEntity
    {
        public long Id { get; init; }

        public bool IsGroup { get; init; }

        public string UserNickname { get; init; }

        public long AuthorizationTime { get; init; }
    }
}
