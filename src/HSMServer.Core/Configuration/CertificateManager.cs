﻿using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace HSMServer.Core.Configuration
{
    public class CertificateManager
    {
        private readonly Logger _logger;
        private readonly TimeSpan _updateInterval;
        private readonly List<CertificateDescriptor> _certificates;
        private DateTime _lastUpdate;
        private object _syncRoot;

        public CertificateManager()
        {
            _updateInterval = TimeSpan.FromSeconds(10);
            _lastUpdate = DateTime.MinValue;
            _syncRoot = new object();
            lock (_syncRoot)
            {
                _certificates = new List<CertificateDescriptor>();
            }
            _logger = LogManager.GetCurrentClassLogger();
            _logger.Info("Certificate manager initialized");
        }

        private IEnumerable<CertificateDescriptor> ReadUserCertificates()
        {
            string certFolderPath = CertificatesConfig.CertificatesFolderPath;

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
                lock (_syncRoot)
                {
                    _certificates.Clear();
                    _certificates.AddRange(ReadUserCertificates());
                }
                _lastUpdate = DateTime.Now;
            }
        }

        public List<CertificateDescriptor> GetUserCertificates()
        {
            UpdateCertificates();

            List<CertificateDescriptor> result = new List<CertificateDescriptor>();
            lock (_syncRoot)
            {
                result.AddRange(_certificates);
            }

            return result;
        }

        public X509Certificate2 GetCertificateByFileName(string fileName)
        {
            UpdateCertificates();

            X509Certificate2 result;

            lock (_syncRoot)
            {
                result = _certificates.FirstOrDefault(d => d.FileName.Equals(fileName))?.Certificate;
            }

            return result;
        }
    }
}
