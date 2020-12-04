using System.Security.Cryptography.X509Certificates;
using HSMClientWPFControls.Model;

namespace HSMClientWPFControls.ConnectorInterface
{
    public interface ISettingsConnector
    {
        public X509Certificate2 GetNewClientCertificate(CreateCertificateModel model);
    }
}