using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record FolderEntity : BaseNodeEntity
    {
        public List<byte[]> TelegramChats { get; init; }

        public int Color { get; init; }
    }
}