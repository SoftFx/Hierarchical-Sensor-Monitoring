using System.Security.Cryptography.X509Certificates;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.DataLayer;
using HSMServer.Model;
using NLog;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public class MonitoringCore : IMonitoringCore
    {
        private readonly IMonitoringQueueManager _queueManager;
        private readonly UserManager _userManager;
        private readonly CertificateManager _certificateManager;
        private readonly ClientCertificateValidator _validator;
        private readonly Logger _logger;

        public MonitoringCore()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _certificateManager = new CertificateManager();
            _validator = new ClientCertificateValidator(_certificateManager);
            _userManager = new UserManager(_certificateManager);
            _queueManager = new MonitoringQueueManager();
            _logger.Debug($"Monitoring core initialized");
        }

        #region Sensor saving

        public void AddSensorInfo(JobResult info)
        {

        }

        public void AddSensorInfo(NewJobResult info)
        {

        }

        #endregion

        #region SensorRequests

        public void GetSensorUpdates(X509Certificate2 clientCertificate)
        {
            _validator.Validate(clientCertificate);

            User user = _userManager.GetUserByCertificateThumbprint(clientCertificate.Thumbprint);
        }

        public void GetSensorsTree(X509Certificate2 clientCertificate)
        {
            _validator.Validate(clientCertificate);


        }

        #endregion
    }
}
