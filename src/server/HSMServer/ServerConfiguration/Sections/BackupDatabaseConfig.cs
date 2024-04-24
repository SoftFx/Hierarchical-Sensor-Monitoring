namespace HSMServer.ServerConfiguration
{
    public class BackupDatabaseConfig
    {
        public const int DefaultPeriodHours = 1;
        public const int DefaultStoragePeriodDays = 10;


        public int PeriodHours { get; set; } = DefaultPeriodHours;

        public int StoragePeriodDays { get; set; } = DefaultStoragePeriodDays;
    }
}
