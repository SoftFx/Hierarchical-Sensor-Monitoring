using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using HSMCommon.Exceptions;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using HSMServer.Exceptions;
using Microsoft.AspNetCore.Http;
using NLog;

namespace HSMServer.Configuration
{
    public class ClientCertificateValidator
    {
        private readonly Logger _logger;
        private readonly CertificateManager _certificateManager;
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(20);
        private readonly List<string> _certificateThumbprints = new List<string>();
        private readonly List<FirstLoginInfo> _firstLoginInfos = new List<FirstLoginInfo>();
        private DateTime _lastUpdate;
        private readonly DateTime _lastFirstLoginInfosUpdate;
        private string _defaultClientCertificateThumbprint;
        public ClientCertificateValidator(CertificateManager certificateManager)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _lastUpdate = DateTime.MinValue;
            _lastFirstLoginInfosUpdate = DateTime.MinValue;
            _certificateManager = certificateManager;
            _defaultClientCertificateThumbprint = _certificateManager.GetDefaultClientCertificateThumbprint();
            _logger.Info("ClientCertificateValidator initialized");
        }

        private List<FirstLoginInfo> FirstLoginInfos
        {
            get
            {
                if (DateTime.Now - _lastFirstLoginInfosUpdate > _updateInterval)
                {
                    _firstLoginInfos.Clear();
                    _firstLoginInfos.AddRange(DatabaseClass.Instance.GetFirstLoginInfos());
                }

                return _firstLoginInfos;
            }
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

        private bool IsDefaultClientForbidden(ConnectionInfo connection)
        {
            return FirstLoginInfos.FirstOrDefault(i => i.Address.Equals(connection.LocalIpAddress)) != null;
        }
    }
}
