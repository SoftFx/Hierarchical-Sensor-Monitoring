using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using HSMServer.Exceptions;
using Microsoft.Extensions.Logging;

namespace HSMServer.Configuration
{
    public class ClientCertificateValidator
    {
        private readonly ILogger<ClientCertificateValidator> _logger;
        private readonly CertificateManager _certificateManager;
        private readonly TimeSpan _updateInterval;
        private readonly List<string> _certificateThumbprints;
        private DateTime _lastUpdate;
        private object _syncRoot;
        public ClientCertificateValidator(CertificateManager certificateManager, ILogger<ClientCertificateValidator> logger)
        {
            _logger = logger;
            _syncRoot = new object();
            _lastUpdate = DateTime.MinValue;
            _updateInterval = TimeSpan.FromSeconds(20);
            _certificateManager = certificateManager;
            lock (_syncRoot)
            {
                _certificateThumbprints = new List<string>();
            }
            _logger.LogInformation("ClientCertificateValidator initialized");
        }

        private void UpdateCertificates()
        {
            lock (_syncRoot)
            {
                _certificateThumbprints.Clear();

                _certificateThumbprints.AddRange(_certificateManager.GetUserCertificates().Select(d => d.Certificate.Thumbprint));
            }
        }

        public bool IsValid(X509Certificate2 clientCertificate)
        {
            if (DateTime.Now - _lastUpdate > _updateInterval)
            {
                UpdateCertificates();
                _lastUpdate = DateTime.Now;
            }

            bool isValid;
            lock (_syncRoot)
            {
                isValid = _certificateThumbprints.Contains(clientCertificate.Thumbprint);
            }

            return isValid;
        }
        public void Validate(X509Certificate2 clientCertificate)
        {
            try
            {
                if (DateTime.Now - _lastUpdate > _updateInterval)
                {
                    UpdateCertificates();
                    _lastUpdate = DateTime.Now;
                }

                bool isCert;
                lock (_syncRoot)
                {
                    isCert = _certificateThumbprints.Contains(clientCertificate.Thumbprint);
                }

                if (isCert)
                {
                    return;
                }

                _logger.LogWarning($"Rejecting certificate: '{clientCertificate.SubjectName.Name}'");

                throw new UserRejectedException(
                    $"User certificate '{clientCertificate.SubjectName.Name}' is wrong, authorization failed.");
            }
            catch (UserRejectedException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"ClientCertificateValidator: validate error = {ex}");
            }
        }
    }
}
