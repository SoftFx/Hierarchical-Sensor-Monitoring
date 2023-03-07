using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed class GroupEntity
    {
        public string Id { get; init; }

        public string AuthorId { get; init; }

        public string DisplayName { get; init; }

        public string Description { get; init; }

        public long CreationDate { get; set; }

        public int Color { get; set; }

        public List<string> Products { get; init; }

        public Dictionary<string, byte> UserRoles { get; set; }
    }
}
