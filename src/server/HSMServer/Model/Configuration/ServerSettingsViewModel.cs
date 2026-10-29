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


        [Display(Name = "Port")]
        public int Port { get; set; }


        public ServerSettingsViewModel() { }

        public ServerSettingsViewModel(IServerConfig config)
        {
            CertificateName = config.ServerCertificate.Name;
            CertificateKey = config.ServerCertificate.Key;

            Port = config.Kestrel.Port;
        }
    }
}
