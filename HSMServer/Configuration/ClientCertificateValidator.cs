using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using HSMServer.Exceptions;
using Microsoft.Extensions.Logging;

namespace HSMServer.Configuration
{
    public class ClientCertificateValidator
    {
        private readonly ILogger<ClientCertificateValidator> _logger;
        private readonly CertificateManager _certificateManager;
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(10);
        private readonly List<string> _certificateThumbprints = new List<string>();
        private DateTime _lastUpdate;
        public ClientCertificateValidator(ILogger<ClientCertificateValidator> logger, CertificateManager certificateManager)
        {
            _logger = logger;
            _lastUpdate = DateTime.MinValue;
            _certificateManager = certificateManager;
        }

        private void UpdateCertificates()
        {
            _certificateThumbprints.Clear();

            _certificateThumbprints.AddRange(_certificateManager.GetUserCertificates().Select(s => s.Thumbprint));
        }

        public void Validate(X509Certificate2 certificate)
        {
            try
            {
                if (DateTime.Now - _lastUpdate > _updateInterval)
                {
                    UpdateCertificates();
                    _lastUpdate = DateTime.Now;
                }

                if (_certificateThumbprints.Contains(certificate.Thumbprint))
                {
                    return;
                }

                _logger.LogWarning($"Rejecting certificate: '{certificate.SubjectName.Name}'");

                throw new UserRejectedException(
                    $"User certificate '{certificate.SubjectName.Name}' is wrong, authorization failed.");
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
