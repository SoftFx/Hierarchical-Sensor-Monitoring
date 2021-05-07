using System.Security.Cryptography.X509Certificates;

namespace HSMServer.Configuration
{
    internal class CertificateDescriptor
    {
        public string FileName { get; set; }
        public X509Certificate2 Certificate { get; set; }
    }
}
