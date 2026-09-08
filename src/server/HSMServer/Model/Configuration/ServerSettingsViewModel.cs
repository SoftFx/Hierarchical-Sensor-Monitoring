using HSMServer.ServerConfiguration;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Configuration
{
    public class ServerSettingsViewModel
    {
        [Display(Name = "Name")]
        public string CertificateName { get; set; }

        [Display(Name = "Key")]
        public string CertificateKey { get; set; }


        [Display(Name = "Sensors API port")]
        public int SensorsPort { get; set; }

        [Display(Name = "Site port")]
        public int SitePort { get; set; }


        [Display(Name = "Enable API tokens")]
        public bool ApiTokensEnabled { get; set; }


        public ServerSettingsViewModel() { }

        public ServerSettingsViewModel(IServerConfig config)
        {
            CertificateName = config.ServerCertificate.Name;
            CertificateKey = config.ServerCertificate.Key;

            SensorsPort = config.Kestrel.SensorPort;
            SitePort = config.Kestrel.SitePort;

            ApiTokensEnabled = config.ApiTokens.Enabled;
        }
    }
}
