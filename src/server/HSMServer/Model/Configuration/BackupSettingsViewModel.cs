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

        [Display(Name = "Enable sftp")]
        public bool IsSftpEnabled { get; set; }

        [Display(Name = "Host name")]
        public string Address { get; set; }

        [Display(Name = "Port number")]
        public int? Port { get; set; }

        [Display(Name = "User name")]
        public string Username { get; set; }
        
        public string Password { get; set; }

        [Display(Name = "Private key")]
        public string PrivateKey { get; set;}

        [Display(Name = "Root path")]
        public string RootPath { get; set; }

        public BackupSettingsViewModel() { }

        public BackupSettingsViewModel(IServerConfig config)
        {
            BackupStoragePeriodDays = config.BackupDatabase.StoragePeriodDays;
            BackupPeriodHours       = config.BackupDatabase.PeriodHours;
            IsEnabled               = config.BackupDatabase.IsEnabled;

            IsSftpEnabled = config.BackupDatabase.SftpConnectionConfig.IsEnabled;
            Address       = config.BackupDatabase.SftpConnectionConfig.Address;
            Port          = config.BackupDatabase.SftpConnectionConfig.Port;
            Username      = config.BackupDatabase.SftpConnectionConfig.Username;
            Password      = config.BackupDatabase.SftpConnectionConfig.Password;
            PrivateKey    = config.BackupDatabase.SftpConnectionConfig.PrivateKey;
            RootPath      = config.BackupDatabase.SftpConnectionConfig.RootPath;
        }
    }
}
