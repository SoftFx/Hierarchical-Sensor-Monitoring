using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public record ChangeInfoNodeEntity
    {
        public Dictionary<string, ChangeInfo> Properties { get; init; } = new();

        public Dictionary<string, ChangeInfo> Settings { get; init; } = new();

        public Dictionary<string, ChangeInfo> Policies { get; init; } = new();


        public ChangeInfo TTLPolicy { get; init; }
    }


    public sealed record ChangeInfo
    {
        public InitiatorInfo Initiator { get; init; }

        public long Time { get; init; }
    }


    public sealed record InitiatorInfo
    {
        public byte Type { get; init; }

        public string Info { get; init; }
    }
}