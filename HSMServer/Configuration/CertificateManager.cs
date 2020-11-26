using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using HSMCommon;
using NLog;

namespace HSMServer.Configuration
{
    public class CertificateManager
    {
        private readonly Logger _logger;
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(10);
        private readonly List<CertificateDescriptor> _certificates = new List<CertificateDescriptor>();
        private readonly DateTime _lastUpdate = DateTime.MinValue;

        public CertificateManager()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _logger.Info("Certificate manager initialized");
        }

        private IEnumerable<CertificateDescriptor> ReadUserCertificates()
        {
            string certFolderPath = Config.CertificatesFolderPath;

            if(!Directory.Exists(certFolderPath))
                yield break;

            string[] files = Directory.GetFiles(certFolderPath, "*.crt");
            foreach (var file in files)
            {
                X509Certificate2 cert = null;
                CertificateDescriptor descriptor = null;
                try
                {
                    cert = new X509Certificate2(file);
                    descriptor = new CertificateDescriptor {Certificate = cert, FileName = Path.GetFileName(file)};
                }
                catch
                {
                    continue;
                }

                yield return descriptor;
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
        public List<CertificateDescriptor> GetUserCertificates()
        {
            UpdateCertificates();

            return _certificates;
        }

        public X509Certificate2 GetCertificateByFileName(string fileName)
        {
            UpdateCertificates();

            return _certificates.FirstOrDefault(d => d.FileName.Equals(fileName))?.Certificate;
        }

        public string GetDefaultClientCertificateThumbprint()
        {
            string certFolderPath = Config.CertificatesFolderPath;

            //if (!Directory.Exists(certFolderPath))

            string[] files = Directory.GetFiles(certFolderPath, "*.crt");
            var defaultClientCertFile =
                files.FirstOrDefault(f => Path.GetFileName(f) == CommonConstants.DefaultClientCrtCertificateName);

            if (!string.IsNullOrEmpty(defaultClientCertFile))
            {
                X509Certificate2 defaultClientCertificate = new X509Certificate2(defaultClientCertFile);
                return defaultClientCertificate.Thumbprint;
            }

            return string.Empty;
        }
    }
}
