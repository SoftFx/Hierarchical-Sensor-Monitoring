namespace HSMServer.ServerConfiguration
{
    public class BackupDatabaseConfig
    {
        public const int DefaultPeriodHours = 1;
        public const int DefaultStoragePeriodDays = 10;
        public const bool DefaultIsBackupEnabled = true;


        public bool IsEnabled { get; set; } = DefaultIsBackupEnabled;

        public int PeriodHours { get; set; } = DefaultPeriodHours;

        public int StoragePeriodDays { get; set; } = DefaultStoragePeriodDays;
    }
}
