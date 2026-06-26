using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{

    public sealed record AlertTemplateEntity
    {
        public List<PolicyEntity> TTLPolicies { get; init; } = [];

        public List<TimeIntervalEntity> TTLs { get; init; } = [];

        // Legacy fields for backward-compatible deserialization
        public PolicyEntity TTLPolicy { get; init; }
        public TimeIntervalEntity TTL { get; init; }

        public List<PolicyEntity> Policies { get; init; }

        public byte[] Id { get; init; }

        public byte SensorType { get; init; }

        public string Name { get; set; }

        public List<string> Paths { get; init; } = [];

        // Legacy field for backward-compatible deserialization
        public string Path { get; init; }

        public Guid FolderId { get; init; }

    }
}