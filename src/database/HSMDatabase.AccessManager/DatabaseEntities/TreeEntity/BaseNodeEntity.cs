using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public abstract record BaseNodeEntity
    {
        public Dictionary<string, TimeIntervalEntity> Settings { get; init; } = new();

        public List<string> Policies { get; init; } = new();


        public required string Id { get; init; }


        public string AuthorId { get; init; }

        public long CreationDate { get; init; }


        public string DisplayName { get; init; }

        public string Description { get; init; }
    }
}
