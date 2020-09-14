using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace HSMServer.Authentication
{
    public class CertificateValidationService
    {
        List<X509Certificate2> _certificates;
        public CertificateValidationService()
        {
            _certificates = new List<X509Certificate2>();
        }

        public bool ValidateCertificate(X509Certificate2 clientCertificate)
        {
            foreach (var certificate in _certificates)
            {
                if (certificate.Thumbprint == clientCertificate.Thumbprint)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
