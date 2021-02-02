using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using HSMServer.Exceptions;
using NLog;

namespace HSMServer.Configuration
{
    public class ClientCertificateValidator
    {
        private readonly Logger _logger;
        private readonly CertificateManager _certificateManager;
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(20);
        private readonly List<string> _certificateThumbprints = new List<string>();
        private DateTime _lastUpdate;
        public ClientCertificateValidator(CertificateManager certificateManager)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _lastUpdate = DateTime.MinValue;
            _certificateManager = certificateManager;
            _logger.Info("ClientCertificateValidator initialized");

            //UpdateCertificates();
        }

        private void UpdateCertificates()
        {
            _certificateThumbprints.Clear();

            _certificateThumbprints.AddRange(_certificateManager.GetUserCertificates().Select(d => d.Certificate.Thumbprint));
        }

        public void Validate(X509Certificate2 clientCertificate)
        {
            try
            {
                //if (connection.ClientCertificate.Thumbprint == _defaultClientCertificateThumbprint)
                //{
                //    if (!IsDefaultClientForbidden(connection))
                //    {
                //        throw new DefaultClientCertificateRejectedException("Default client certificate for the current address rejected!");
                //    }

                //    return;
                //}

                if (DateTime.Now - _lastUpdate > _updateInterval)
                {
                    UpdateCertificates();
                    _lastUpdate = DateTime.Now;
                }

                if (_certificateThumbprints.Contains(clientCertificate.Thumbprint))
                {
                    return;
                }

                _logger.Warn($"Rejecting certificate: '{clientCertificate.SubjectName.Name}'");

                throw new UserRejectedException(
                    $"User certificate '{clientCertificate.SubjectName.Name}' is wrong, authorization failed.");
            }
            catch (UserRejectedException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"ClientCertificateValidator: validate error = {ex}");
            }
        }
    }
}
