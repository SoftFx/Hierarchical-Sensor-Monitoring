using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{

    public sealed record AlertTemplateEntity
    {
        public PolicyEntity TTLPolicy { get; init; }

        public TimeIntervalEntity TTL { get; init; }

        public List<PolicyEntity> Policies { get; init; }

        public byte[] Id { get; init; }

        public byte SensorType { get; init; }

        public string Name { get; set; }

        public string Path { get; init; }

    }
}