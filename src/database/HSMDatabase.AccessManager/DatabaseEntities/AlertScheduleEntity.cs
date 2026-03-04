

namespace HSMDatabase.AccessManager.DatabaseEntities
{

    public sealed record AlertScheduleEntity
    {
        public byte[] Id { get; set; }

        public string Name { get; set; }

        public string TimeZone { get; set; }
        public string Schedule { get; set; }
    }

}