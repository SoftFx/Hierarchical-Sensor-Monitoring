using System.Security.Cryptography.X509Certificates;

namespace HSMClient.Configuration
{
    public class ConnectionInfo
    {
        public string Address { get; set; }
        public string Port { get; set; }
        public X509Certificate2 ClientCertificate { get; set; }
        public string CertificateFileName { get; set; }
    }
}
