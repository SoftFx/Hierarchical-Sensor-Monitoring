using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class NotificationSettingsEntity
    {
        public TelegramSettingsEntity TelegramSettings { get; init; }

        public Dictionary<string, long> IgnoredSensors { get; init; }
        
        public List<string> EnabledSensors { get; init; }
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

        public string Name { get; init; }

        public bool IsUserChat { get; init; }

        public long AuthorizationTime { get; init; }
    }
}
