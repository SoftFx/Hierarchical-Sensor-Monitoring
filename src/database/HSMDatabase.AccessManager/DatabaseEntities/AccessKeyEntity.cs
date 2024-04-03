using System.Text.Json.Serialization;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record AccessKeyEntity
    {
        public string Id { get; init; }

        public string AuthorId { get; init; }

        public string ProductId { get; init; }

        [JsonPropertyName("KeyState")]
        public byte State { get; init; }

        [JsonPropertyName("KeyPermissions")]
        public long Permissions { get; init; }

        public string DisplayName { get; init; }

        public long CreationTime { get; init; }

        public long ExpirationTime { get; init; }
    }
}