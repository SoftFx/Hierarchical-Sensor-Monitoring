using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using NLog;
using Org.BouncyCastle.Security;

namespace HSMServer.Configuration
{
    public class CertificateManager
    {
        private readonly Logger _logger;
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(10);
        private readonly List<X509Certificate2> _certificates = new List<X509Certificate2>();
        private readonly DateTime _lastUpdate = DateTime.MinValue;

        public CertificateManager()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _logger.Info("Certificate manager initialized");
        }

        private IEnumerable<X509Certificate2> ReadUserCertificates()
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

        private void UpdateCertificates()
        {
            if (DateTime.Now - _lastUpdate > _updateInterval)
            {
                _certificates.Clear();
                _certificates.AddRange(ReadUserCertificates());
            }
        }
        public List<X509Certificate2> GetUserCertificates()
        {
            UpdateCertificates();

            return _certificates;
        }

        public X509Certificate2 GetCertificateBySubject(string subject)
        {
            UpdateCertificates();

            return _certificates.FirstOrDefault(cert => cert.SubjectName.Name.Equals(subject));
        }
    }
}
