using HSMServer.ServerConfiguration;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Configuration
{
    public sealed class BackupSettingsViewModel
    {
        [Display(Name = "Storage time")]
        public int BackupStoragePeriodDays { get; set; }

        [Display(Name = "Periodicity")]
        public int BackupPeriodHours { get; set; }

        [Display(Name = "Enable backup")]
        public bool IsEnabled { get; set; }


        public BackupSettingsViewModel() { }

        public BackupSettingsViewModel(IServerConfig config)
        {
            BackupStoragePeriodDays = config.BackupDatabase.StoragePeriodDays;
            BackupPeriodHours = config.BackupDatabase.PeriodHours;
            IsEnabled = config.BackupDatabase.IsEnabled;
        }
    }
}
