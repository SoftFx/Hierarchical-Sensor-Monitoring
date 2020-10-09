using System.Security.Cryptography.X509Certificates;
using HSMCommon.Keys;
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
            _logger.Debug("Monitoring core initialized");
        }

        #region Sensor saving

        public void AddSensorInfo(JobResult info)
        {

        }

        public string AddSensorInfo(NewJobResult info)
        {
            SensorUpdateMessage updateMessage = Converter.ConvertToSend(info);
            _queueManager.AddSensorData(updateMessage);

            var convertedInfo = Converter.ConvertToInfo(info);
            string key = DatabaseClass.Instance.GetSensorKey(info.ServerName, info.SensorName);
            if (string.IsNullOrEmpty(key))
            {
                key = SensorKeyGenerator.GenerateKey(info.ServerName, info.SensorName);
            }

            convertedInfo.Key = key;
            DatabaseClass.Instance.AddSensor(convertedInfo);
            return key;
        }

        #endregion

        #region SensorRequests

        public SensorsService.SensorsUpdateMessage GetSensorUpdates(X509Certificate2 clientCertificate)
        {
            _validator.Validate(clientCertificate);

            User user = _userManager.GetUserByCertificateThumbprint(clientCertificate.Thumbprint);
            SensorsUpdateMessage sensorsUpdateMessage = new SensorsUpdateMessage();
            sensorsUpdateMessage.Sensors.AddRange(_queueManager.GetUserUpdates(user));
            return sensorsUpdateMessage;
        }

        public SensorsTreeMessage GetSensorsTree(X509Certificate2 clientCertificate)
        {
            _validator.Validate(clientCertificate);

            User user = _userManager.GetUserByCertificateThumbprint(clientCertificate.Thumbprint);

            return new SensorsTreeMessage();
        }

        #endregion
    }
}
