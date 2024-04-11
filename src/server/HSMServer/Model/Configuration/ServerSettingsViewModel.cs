using HSMServer.ServerConfiguration;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Configuration
{
    public class ServerSettingsViewModel
    {
        [Display(Name = "Storage time")]
        public int BackupStoragePeriodDays { get; set; }

        [Display(Name = "Periodicity")]
        public int BackupPeriodHours { get; set; }


        [Display(Name = "Name")]
        public string CertificateName { get; set; }

        [Display(Name = "Key")]
        public string CertificateKey { get; set; }


        [Display(Name = "Sensors API port")]
        public int SensorsPort { get; set; }

        [Display(Name = "Site port")]
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
