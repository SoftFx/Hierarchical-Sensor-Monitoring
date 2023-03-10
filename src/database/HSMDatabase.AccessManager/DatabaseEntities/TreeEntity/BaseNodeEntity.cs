using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public abstract record BaseNodeEntity
    {
        public List<string> Policies { get; init; }


        public string Id { get; init; }

        public string AuthorId { get; init; }

        public long CreationDate { get; init; }


        public string DisplayName { get; init; }

        public string Description { get; init; }
    }
}
