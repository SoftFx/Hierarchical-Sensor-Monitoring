using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record PolicyTargetEntity(byte Type, string Value);


    public sealed record PolicyConditionEntity
    {
        public PolicyTargetEntity Target { get; init; }

        public byte Combination { get; init; }

        public byte Operation { get; init; }

        public byte Property { get; init; }
    }


    public sealed record PolicyEntity
    {
        public List<PolicyConditionEntity> Conditions { get; init; }

        public TimeIntervalEntity Sensitivity { get; init; }

        public byte[] Id { get; init; }

        public byte SensorStatus { get; init; }

        public string Template { get; init; }

        public string Icon { get; init; }
    }
}