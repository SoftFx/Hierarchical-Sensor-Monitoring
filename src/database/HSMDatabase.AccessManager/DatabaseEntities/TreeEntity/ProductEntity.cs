using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record ProductEntity : BaseNodeEntity
    {
        [Obsolete("Should be removed after telegram chats migration")]
        public NotificationSettingsEntity NotificationSettings { get; init; }

        public List<string> TelegramChats { get; init; }


        public string ParentProductId { get; init; }

        public string FolderId { get; init; }

        public int State { get; init; }
    }
}