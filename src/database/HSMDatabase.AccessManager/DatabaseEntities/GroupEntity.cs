using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed class GroupEntity
    {
        public List<string> ProductIds { get; init; }

        public Dictionary<string, byte> UserRoles { get; init; }


        public string Id { get; init; }

        public string AuthorId { get; init; }

        public long CreationDate { get; init; }


        public string DisplayName { get; init; }

        public string Description { get; init; }

        public int Color { get; init; }
    }
}
