using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using NLog;

namespace HSMServer.Configuration
{
    public class CertificateManager
    {
        private readonly Logger _logger;

        public CertificateManager()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _logger.Info("Certificate manager initialized");
        }

        public IEnumerable<X509Certificate2> GetUserCertificates()
        {
            string certFolderPath = Config.CertificatesFolderPath;

            if(!Directory.Exists(certFolderPath))
                yield break;

            string[] files = Directory.GetFiles(certFolderPath, "*.crt");
            foreach (var file in files)
            {
                X509Certificate2 cert = null;
                try
                {
                    cert = new X509Certificate2(file);
                }
                catch
                {
                    continue;
                }

                yield return cert;
            }
        }
    }
}
