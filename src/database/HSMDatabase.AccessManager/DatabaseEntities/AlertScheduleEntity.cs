

namespace HSMDatabase.AccessManager.DatabaseEntities
{

    public sealed record AlertScheduleEntity
    {
        public byte[] Id { get; set; }

        public string Name { get; set; }

        public string Timezone { get; set; }
        public string Schedule { get; set; }

    }

}