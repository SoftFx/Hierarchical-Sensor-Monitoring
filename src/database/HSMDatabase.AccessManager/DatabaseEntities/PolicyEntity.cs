namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed record OldPolicyEntity
    {
        public string Id { get; init; }

        public byte[] Policy { get; init; }
    }


    public sealed record PolicyTargetEntity(byte Type, string Value);


    public sealed record PolicyEntity
    {
        public byte[] Id { get; init; }


        public byte Operation { get; init; }

        public byte SensorStatus { get; init; }


        public PolicyTargetEntity Target { get; init; }


        public string Property { get; init; }

        public string Template { get; init; }

        public string Icon { get; init; }
    }
}