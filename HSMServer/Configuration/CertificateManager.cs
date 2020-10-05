using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HSMServer.Configuration
{
    public class CertificateManager
    {
        private readonly ILogger<CertificateManager> _logger;

        public CertificateManager(ILogger<CertificateManager> logger)
        {
            _logger = logger;
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
