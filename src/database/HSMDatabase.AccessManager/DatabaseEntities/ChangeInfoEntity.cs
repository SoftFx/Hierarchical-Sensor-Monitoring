using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public record ChangeInfoTableEntity
    {
        public Dictionary<string, ChangeInfoEntity> Properties { get; init; } = new();

        public Dictionary<string, ChangeInfoEntity> Settings { get; init; } = new();

        public Dictionary<string, ChangeInfoEntity> Policies { get; init; } = new();


        public ChangeInfoEntity TTLPolicy { get; init; } = new();
    }


    public sealed record ChangeInfoEntity
    {
        public InitiatorInfoEntity Initiator { get; init; }

        public int PropertyVersion { get; init; }

        public long Time { get; init; }
    }


    public sealed record InitiatorInfoEntity
    {
        public byte Type { get; init; }

        public string Info { get; init; }
    }
}