using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using HSMServer.Model;
using HSMServer.Products;
using NLog;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public class MonitoringCore : IMonitoringCore, IDisposable
    {
        #region IDisposable implementation

        private bool _disposed;

        // Implement IDisposable.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposingManagedResources)
        {
            // The idea here is that Dispose(Boolean) knows whether it is 
            // being called to do explicit cleanup (the Boolean is true) 
            // versus being called due to a garbage collection (the Boolean 
            // is false). This distinction is useful because, when being 
            // disposed explicitly, the Dispose(Boolean) method can safely 
            // execute code using reference type fields that refer to other 
            // objects knowing for sure that these other objects have not been 
            // finalized or disposed of yet. When the Boolean is false, 
            // the Dispose(Boolean) method should not execute code that 
            // refer to reference type fields because those objects may 
            // have already been finalized."

            if (!_disposed)
            {
                if (disposingManagedResources)
                {

                }

                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~MonitoringCore()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion

        private readonly IMonitoringQueueManager _queueManager;
        private readonly UserManager _userManager;
        private readonly CertificateManager _certificateManager;
        private readonly ClientCertificateValidator _validator;
        private readonly ProductManager _productManager;
        private readonly Logger _logger;
        public readonly char[] _pathSeparator = new[] { '/' };

        public MonitoringCore()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _certificateManager = new CertificateManager();
            _validator = new ClientCertificateValidator(_certificateManager);
            _userManager = new UserManager(_certificateManager);
            _queueManager = new MonitoringQueueManager();
            _productManager = new ProductManager();
            _logger.Debug("Monitoring core initialized");
        }

        #region Sensor saving

        public void AddSensorInfo(JobResult info)
        {
            SensorUpdateMessage updateMessage = Converter.Convert(info);
            _queueManager.AddSensorData(updateMessage);

            SensorDataObject obj = Converter.ConvertToDatabase(info);
            string productName = _productManager.GetProductNameByKey(info.Key);
            string sensorName = ExtractSensorName(info.Path);
            if (!_productManager.IsSensorRegistered(productName, sensorName))
            {
                _productManager.AddSensor(new SensorInfo(){ Path = info.Path, ProductName = productName, SensorName = sensorName });
            }

            ThreadPool.QueueUserWorkItem(_ => DatabaseClass.Instance.WriteSensorData(obj, productName, sensorName));
        }

        //public string AddSensorInfo(NewJobResult info)
        //{
        //    SensorUpdateMessage updateMessage = Converter.Convert(info);
        //    _queueManager.AddSensorData(updateMessage);

        //    var convertedInfo = Converter.ConvertToInfo(info);
            
        //    ThreadPool.QueueUserWorkItem(_ => DatabaseClass.Instance.AddSensor(convertedInfo));
        //    //DatabaseClass.Instance.AddSensor(convertedInfo);
        //    return key;
        //}

        #endregion

        #region SensorRequests

        public SensorsUpdateMessage GetSensorUpdates(X509Certificate2 clientCertificate)
        {
            _validator.Validate(clientCertificate);

            User user = _userManager.GetUserByCertificateThumbprint(clientCertificate.Thumbprint);
            SensorsUpdateMessage sensorsUpdateMessage = new SensorsUpdateMessage();
            sensorsUpdateMessage.Sensors.AddRange(_queueManager.GetUserUpdates(user));
            return sensorsUpdateMessage;
        }

        public SensorsUpdateMessage GetAllAvailableSensorsUpdates(X509Certificate2 clientCertificate)
        {
            _validator.Validate(clientCertificate);

            User user = _userManager.GetUserByCertificateThumbprint(clientCertificate.Thumbprint);
            SensorsUpdateMessage sensorsUpdateMessage = new SensorsUpdateMessage();
            //TODO: Read updates for ALL available sensors for the current user
            foreach (var permission in user.UserPermissions)
            {
                var sensorsList = DatabaseClass.Instance.GetSensorsList(permission.ProductName);
                foreach (var sensor in sensorsList)
                {
                    var lastVal = DatabaseClass.Instance.GetLastSensorValue(permission.ProductName, sensor);
                    if (lastVal != null)
                    {
                        sensorsUpdateMessage.Sensors.Add(Converter.Convert(lastVal));
                    }
                }
            }
            return sensorsUpdateMessage;
        }

        #endregion

        #region Sub-methods

        public string ExtractSensorName(string path)
        {
            var splitRes = path.Split(_pathSeparator);
            return splitRes[^1];
        }

        #endregion
    }
}
