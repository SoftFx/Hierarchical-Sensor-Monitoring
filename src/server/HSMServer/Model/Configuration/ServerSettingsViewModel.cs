using HSMServer.ServerConfiguration;

namespace HSMServer.Model.Configuration
{
    public class ServerSettingsViewModel
    {
        public int BackupStoragePeriodDays { get; set; }

        public int BackupPeriodHours { get; set; }


        public string CertificateName { get; set; }

        public string CertificateKey { get; set; }


        public int SensorsPort { get; set; }

        public int SitePort { get; set; }


        public ServerSettingsViewModel() { }

        public ServerSettingsViewModel(IServerConfig config)
        {
            BackupStoragePeriodDays = config.BackupDatabase.StoragePeriodDays;
            BackupPeriodHours = config.BackupDatabase.PeriodHours;

            CertificateName = config.ServerCertificate.Name;
            CertificateKey = config.ServerCertificate.Key;

            SensorsPort = config.Kestrel.SensorPort;
            SitePort = config.Kestrel.SitePort;
        }
    }
}
