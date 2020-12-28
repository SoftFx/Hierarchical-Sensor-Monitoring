using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using HSMCommon.Certificates;
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
            string productName = _productManager.GetProductNameByKey(info.Key);

            DateTime timeCollected = DateTime.Now;

            SensorUpdateMessage updateMessage = Converter.Convert(info, productName, timeCollected);
            _queueManager.AddSensorData(updateMessage);

            SensorDataObject obj = Converter.ConvertToDatabase(info, timeCollected);

            string sensorName = updateMessage.Name;
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
            if (!_queueManager.IsUserRegistered(user))
            {
                _queueManager.AddUserSession(user);
            }
            SensorsUpdateMessage sensorsUpdateMessage = new SensorsUpdateMessage();
            //TODO: use products permissions later
            //foreach (var permission in user.UserPermissions)
            //{
            //    var sensorsList = DatabaseClass.Instance.GetSensorsList(permission.ProductName);
            //    foreach (var sensor in sensorsList)
            //    {
            //        var lastVal = DatabaseClass.Instance.GetLastSensorValue(permission.ProductName, sensor);
            //        if (lastVal != null)
            //        {
            //            sensorsUpdateMessage.Sensors.Add(Converter.Convert(lastVal, permission.ProductName));
            //        }
            //    }
            //}
            //Let client see all available products by default
            var productsList = _productManager.Products.Select(p => p.Name);
            foreach (var product in productsList)
            {
                var sensorsList = DatabaseClass.Instance.GetSensorsList(product);
                foreach (var sensor in sensorsList)
                {
                    var lastVal = DatabaseClass.Instance.GetLastSensorValue(product, sensor);
                    if (lastVal != null)
                    {
                        sensorsUpdateMessage.Sensors.Add(Converter.Convert(lastVal, product));
                    }
                }
            }
            return sensorsUpdateMessage;
        }

        public SensorsUpdateMessage GetSensorHistory(X509Certificate2 clientCertificate, GetSensorHistoryMessage getHistoryMessage)
        {
            _validator.Validate(clientCertificate);

            User user = _userManager.GetUserByCertificateThumbprint(clientCertificate.Thumbprint);

            SensorsUpdateMessage sensorsUpdate = new SensorsUpdateMessage();
            List<SensorDataObject> dataList = DatabaseClass.Instance.GetSensorDataHistory(getHistoryMessage.Product,
                getHistoryMessage.Name, getHistoryMessage.N);
            sensorsUpdate.Sensors.AddRange(dataList.Select(s => Converter.Convert(s, getHistoryMessage.Product)));
            return sensorsUpdate;
        }

        #endregion

        #region Products

        public ProductsListMessage GetProductsList(X509Certificate2 clientCertificate)
        {
            _validator.Validate(clientCertificate);

            User user = _userManager.GetUserByCertificateThumbprint(clientCertificate.Thumbprint);
            var products = _productManager.Products;
            //TODO: Add filtering list according to User permissions

            ProductsListMessage message = new ProductsListMessage();
            message.Products.AddRange(products.Select(Converter.Convert));
            return message;
        }


        public AddProductResultMessage AddNewProduct(X509Certificate2 clientCertificate, AddProductMessage message)
        {
            _validator.Validate(clientCertificate);

            User user = _userManager.GetUserByCertificateThumbprint(clientCertificate.Thumbprint);
            //TODO: check whether user can add products

            AddProductResultMessage result = new AddProductResultMessage();
            try
            {
                _productManager.AddProduct(message.Name);

                Product product = _productManager.GetProductByName(message.Name);

                result.Result = true;
                result.ProductData = Converter.Convert(product);
            }
            catch (Exception e)
            {
                result.Result = false;
                result.Error = e.Message;
                _logger.Error(e, $"Failed to add new product name = {message.Name}, user = {user.UserName}");
            }

            return result;
        }

        public RemoveProductResultMessage RemoveProduct(X509Certificate2 clientCertificate,
            RemoveProductMessage message)
        {
            _validator.Validate(clientCertificate);

            User user = _userManager.GetUserByCertificateThumbprint(clientCertificate.Thumbprint);
            //TODO: check whether user can add products and is the product available for user

            RemoveProductResultMessage result = new RemoveProductResultMessage();
            try
            {
                result.ProductData = Converter.Convert(_productManager.GetProductByName(message.Name));
                _productManager.RemoveProduct(message.Name);
                result.Result = true;
            }
            catch (Exception e)
            {
                result.Result = false;
                result.Error = e.Message;
                _logger.Error(e, $"Failed to remove product name = {message.Name}, user = {user.UserName}");
            }

            return result;
        }

        #endregion

        //public SignedCertificateMessage SignClientCertificate(X509Certificate2 clientCertificate,
        //    CertificateSignRequestMessage request)
        //{
        //    _validator.Validate(clientCertificate);

        //    User user = _userManager.GetUserByCertificateThumbprint(clientCertificate.Thumbprint);
        //    var bytes = request.RequestBytes.ToByteArray();

        //    Pkcs10CertificationRequest certRequest = new Pkcs10CertificationRequest(bytes);
        //    Org.BouncyCastle.X509.X509Certificate signedCertificate =
        //        CertificatesProcessor.SignCertificate(certRequest, Config.CACertificate, Config.CAKeyFilePath);

        //    X509Certificate certV1 = DotNetUtilities.ToX509Certificate(signedCertificate);
        //    X509Certificate2 certV2 = new X509Certificate2(certV1);
        //    //TODO: add new certificate & user
        //    string fileName = $"{request.CommonName}.crt";
        //    _certificateManager.InstallClientCertificate(certV2);
        //    _certificateManager.SaveClientCertificate(certV2, fileName);
        //    _userManager.AddNewUser(request.CommonName, certV2.Thumbprint, fileName);
        //    return Converter.Convert(certV2, Config.CACertificate);
        //}
        public SignedCertificateMessage SignClientCertificate(X509Certificate2 clientCertificate,
            CertificateSignRequestMessage request)
        {
            _validator.Validate(clientCertificate);

            var rsa = RSA.Create(Converter.Convert(request.RSAParameters));

            X509Certificate2 clientCert =
                CertificatesProcessor.CreateAndSignCertificate(request.Subject, rsa, Config.CACertificate);

            string fileName = $"{request.CommonName}.crt";
            _certificateManager.InstallClientCertificate(clientCert);
            _certificateManager.SaveClientCertificate(clientCert, fileName);
            _userManager.AddNewUser(request.CommonName, clientCert.Thumbprint, fileName);
            return Converter.Convert(clientCert, Config.CACertificate);
        }

        #region Sub-methods

        #endregion
    }
}
