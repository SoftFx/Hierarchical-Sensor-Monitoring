using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    [Obsolete("Should be removed after telegram chats migration")]
    public class NotificationSettingsEntity
    {
        public TelegramSettingsEntityOld TelegramSettings { get; init; }
    }


    [Obsolete("Should be removed after telegram chats migration")]
    public sealed class TelegramSettingsEntityOld
    {
        public List<TelegramChatEntityOld> Chats { get; init; }
    }


    [Obsolete("Should be removed after telegram chats migration")]
    public sealed class TelegramChatEntityOld
    {
        public byte[] SystemId { get; init; }

        public long Id { get; init; }


        public string Name { get; init; }

        public bool IsUserChat { get; init; }

        public long AuthorizationTime { get; init; }
    }
}
