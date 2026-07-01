using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record FolderEntity : BaseNodeEntity
    {
        public List<byte[]> Chats { get; init; }

        public int Color { get; init; }

        [JsonInclude]
        [JsonPropertyName("TelegramChats")]
        public List<byte[]> LegacyTelegramChats { get; init; }

        [JsonInclude]
        [JsonPropertyName("SlackDestinations")]
        public List<byte[]> LegacySlackDestinations { get; init; }
    }
}
