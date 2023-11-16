namespace HSMServer.ServerConfiguration
{
    public class BackupDatabaseConfig
    {
        public int PeriodHours { get; set; } = 1;

        public int StoragePeriodDays { get; set; } = 3;
    }
}
