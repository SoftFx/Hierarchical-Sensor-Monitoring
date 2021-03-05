using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Certificates;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using HSMServer.Model;
using HSMServer.Products;
using HSMService;
using NLog;

namespace HSMServer.MonitoringServerCore
{
    public class MonitoringCore : IMonitoringCore
    {
        //#region IDisposable implementation

        //private bool _disposed;

        //// Implement IDisposable.
        //public void Dispose()
        //{
        //    Dispose(true);
        //    GC.SuppressFinalize(this);
        //}

        //protected virtual void Dispose(bool disposingManagedResources)
        //{
        //    // The idea here is that Dispose(Boolean) knows whether it is 
        //    // being called to do explicit cleanup (the Boolean is true) 
        //    // versus being called due to a garbage collection (the Boolean 
        //    // is false). This distinction is useful because, when being 
        //    // disposed explicitly, the Dispose(Boolean) method can safely 
        //    // execute code using reference type fields that refer to other 
        //    // objects knowing for sure that these other objects have not been 
        //    // finalized or disposed of yet. When the Boolean is false, 
        //    // the Dispose(Boolean) method should not execute code that 
        //    // refer to reference type fields because those objects may 
        //    // have already been finalized."

        //    if (!_disposed)
        //    {
        //        if (disposingManagedResources)
        //        {

        //        }

        //        _disposed = true;
        //    }
        //}

        //// Use C# destructor syntax for finalization code.
        //~MonitoringCore()
        //{
        //    // Simply call Dispose(false).
        //    Dispose(false);
        //}

        //#endregion

        private readonly IBarSensorsStorage _barsStorage;
        private readonly IMonitoringQueueManager _queueManager;
        private readonly UserManager _userManager;
        private readonly CertificateManager _certificateManager;
        private readonly ProductManager _productManager;
        private readonly Logger _logger;
        public readonly char[] _pathSeparator = new[] { '/' };

        public MonitoringCore(UserManager userManager, IBarSensorsStorage barsStorage)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _barsStorage = barsStorage;
            _barsStorage.IncompleteBarOutdated += BarsStorage_IncompleteBarOutdated;
            _certificateManager = new CertificateManager();
            _userManager = userManager;
            _queueManager = new MonitoringQueueManager();
            _productManager = new ProductManager();
            _logger.Debug("Monitoring core initialized");
        }

        #region Sensor saving
        private void BarsStorage_IncompleteBarOutdated(object sender, ExtendedBarSensorData e)
        {
            switch (e.ValueType)
            {
                case SensorType.IntegerBarSensor:
                {
                    var typedValue = e.Value as IntBarSensorValue;
                    typedValue.EndTime = DateTime.Now;
                    SensorDataObject obj = Converter.ConvertToDatabase(typedValue, e.TimeCollected);
                    SaveSensorValue(obj, e.ProductName);
                    break;
                }
                case SensorType.DoubleBarSensor:
                {
                    var typedValue = e.Value as DoubleBarSensorValue;
                    typedValue.EndTime = DateTime.Now;
                    SensorDataObject obj = Converter.ConvertToDatabase(typedValue, e.TimeCollected);
                    SaveSensorValue(obj, e.ProductName);
                    break;
                }
            }
        }
        private void SaveSensorValue(SensorDataObject dataObject, string productName)
        {
            if (!_productManager.IsSensorRegistered(productName, dataObject.Path))
            {
                _productManager.AddSensor(productName, dataObject.Path);
            }

            Task.Run(() => DatabaseClass.Instance.WriteSensorData(dataObject, productName));
        }

        private async Task<bool> SaveSensorValueAsync(SensorDataObject dataObject, string productName)
        {
            try
            {
                if (!_productManager.IsSensorRegistered(productName, dataObject.Path))
                {
                    _productManager.AddSensor(productName, dataObject.Path);
                }

                await Task.Run(() => DatabaseClass.Instance.WriteSensorData(dataObject, productName));
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
            
        }
        //private void SaveSensorValue(SensorUpdateMessage updateMessage, string productName, DateTime originalTime)
        //{
        //    SensorDataObject obj = Converter.ConvertToDatabase(updateMessage, originalTime);

        //    if (!_productManager.IsSensorRegistered(productName, obj.Path))
        //    {
        //        //_productManager.AddSensor(new SensorInfo() { Path = updateMessage.Path, ProductName = productName, SensorName = updateMessage.Name });
        //        _productManager.AddSensor(productName, obj.Path);
        //    }

        //    //ThreadPool.QueueUserWorkItem(_ => DatabaseClass.Instance.WriteSensorData(obj, productName));
        //    Task.Run(() => DatabaseClass.Instance.WriteSensorData(obj, productName));
        //}

 

        public async Task<bool> AddSensorValueAsync(BoolSensorValue value)
        {
            string productName = _productManager.GetProductNameByKey(value.Key);

            DateTime timeCollected = DateTime.Now;
            SensorUpdateMessage updateMessage = Converter.Convert(value, productName, timeCollected);
            _queueManager.AddSensorData(updateMessage);

            SensorDataObject dataObject = Converter.ConvertToDatabase(value, timeCollected);
            return await SaveSensorValueAsync(dataObject, productName);
        }

        public void AddSensorsValues(IEnumerable<CommonSensorValue> values)
        {
            var commonSensorValues = values.ToList();
            foreach (var value in commonSensorValues)
            {
                switch (value.SensorType)
                {
                    case SensorType.IntegerBarSensor:
                    {
                        var typedValue = Converter.GetIntBarSensorValue(value.TypedValue.Replace("\\", ""));
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.DoubleBarSensor:
                    {
                        var typedValue = Converter.GetDoubleBarSensorValue(value.TypedValue);
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.DoubleSensor:
                    {
                        var typedValue = Converter.GetDoubleSensorValue(value.TypedValue);
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.IntSensor:
                    {
                        var typedValue = Converter.GetIntSensorValue(value.TypedValue);
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.BooleanSensor:
                    {
                        var typedValue = Converter.GetBoolSensorValue(value.TypedValue.Replace("\\", ""));
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.StringSensor:
                    {
                        var typedValue = Converter.GetStringSensorValue(value.TypedValue);
                        AddSensorValue(typedValue);
                        break;
                    }
                }
            }
        }

        public void AddSensorValue(BoolSensorValue value)
        {
            string productName = _productManager.GetProductNameByKey(value.Key);

            DateTime timeCollected = DateTime.Now;
            SensorUpdateMessage updateMessage = Converter.Convert(value, productName, timeCollected);
            _queueManager.AddSensorData(updateMessage);

            SensorDataObject dataObject = Converter.ConvertToDatabase(value, timeCollected);
            ThreadPool.QueueUserWorkItem(_ => SaveSensorValue(dataObject, productName));
            //SaveSensorValue(dataObject, productName);
        }
        public void AddSensorValue(IntSensorValue value)
        {
            string productName = _productManager.GetProductNameByKey(value.Key);

            DateTime timeCollected = DateTime.Now;
            SensorUpdateMessage updateMessage = Converter.Convert(value, productName, timeCollected);
            _queueManager.AddSensorData(updateMessage);

            SensorDataObject dataObject = Converter.ConvertToDatabase(value, timeCollected);
            ThreadPool.QueueUserWorkItem(_ => SaveSensorValue(dataObject, productName));
            //SaveSensorValue(dataObject, productName);
        }

        public void AddSensorValue(DoubleSensorValue value)
        {
            string productName = _productManager.GetProductNameByKey(value.Key);

            DateTime timeCollected = DateTime.Now;
            SensorUpdateMessage updateMessage = Converter.Convert(value, productName, timeCollected);
            _queueManager.AddSensorData(updateMessage);

            SensorDataObject dataObject = Converter.ConvertToDatabase(value, timeCollected);
            ThreadPool.QueueUserWorkItem(_ => SaveSensorValue(dataObject, productName));
            //SaveSensorValue(dataObject, productName);
        }

        public void AddSensorValue(StringSensorValue value)
        {
            string productName = _productManager.GetProductNameByKey(value.Key);

            DateTime timeCollected = DateTime.Now;
            SensorUpdateMessage updateMessage = Converter.Convert(value, productName, timeCollected);
            _queueManager.AddSensorData(updateMessage);

            SensorDataObject dataObject = Converter.ConvertToDatabase(value, timeCollected);
            ThreadPool.QueueUserWorkItem(_ => SaveSensorValue(dataObject, productName));
            //SaveSensorValue(dataObject, productName);
        }

        public void AddSensorValue(IntBarSensorValue value)
        {
            string productName = _productManager.GetProductNameByKey(value.Key);

            DateTime timeCollected = DateTime.Now;
            SensorUpdateMessage updateMessage = Converter.Convert(value, productName, timeCollected);
            _queueManager.AddSensorData(updateMessage);

            //Skip 
            if (value.EndTime == DateTime.MinValue)
            {
                _barsStorage.Add(value, updateMessage.Product, timeCollected);
                return;
            }

            SensorDataObject dataObject = Converter.ConvertToDatabase(value, timeCollected);
            ThreadPool.QueueUserWorkItem(_ => SaveSensorValue(dataObject, productName));
            //SaveSensorValue(dataObject, productName);
        }

        public void AddSensorValue(DoubleBarSensorValue value)
        {
            string productName = _productManager.GetProductNameByKey(value.Key);

            DateTime timeCollected = DateTime.Now;
            SensorUpdateMessage updateMessage = Converter.Convert(value, productName, timeCollected);
            _queueManager.AddSensorData(updateMessage);

            if (value.EndTime == DateTime.MinValue)
            {
                _barsStorage.Add(value, updateMessage.Product, timeCollected);
                return;
            }

            SensorDataObject dataObject = Converter.ConvertToDatabase(value, timeCollected);
            ThreadPool.QueueUserWorkItem(_ => SaveSensorValue(dataObject, productName));
            //SaveSensorValue(dataObject, productName);
        }


        //public string AddSensorInfo(NewJobResult value)
        //{
        //    SensorUpdateMessage updateMessage = Converter.Convert(value);
        //    _queueManager.AddSensorData(updateMessage);

        //    var convertedInfo = Converter.ConvertToInfo(value);
            
        //    ThreadPool.QueueUserWorkItem(_ => DatabaseClass.Instance.AddSensor(convertedInfo));
        //    //DatabaseClass.Instance.AddSensor(convertedInfo);
        //    return key;
        //}

        #endregion

        public SensorsUpdateMessage GetSensorUpdates(User user)
        {
            SensorsUpdateMessage updateMessage = new SensorsUpdateMessage();
            updateMessage.Sensors.AddRange(_queueManager.GetUserUpdates(user));
            return updateMessage;
        }

        public SensorsUpdateMessage GetSensorsTree(User user)
        {
            if (!_queueManager.IsUserRegistered(user))
            {
                _queueManager.AddUserSession(user);
            }

            SensorsUpdateMessage sensorsUpdateMessage = new SensorsUpdateMessage();
            var productsList = _productManager.Products;
            foreach (var product in productsList)
            {
                var sensorsList = _productManager.GetProductSensors(product.Name);
                foreach (var sensorPath in sensorsList)
                {
                    var lastVal = DatabaseClass.Instance.GetLastSensorValue(product.Name, sensorPath);
                    if (lastVal != null)
                    {
                        sensorsUpdateMessage.Sensors.Add(Converter.Convert(lastVal, product.Name));
                    }
                }
            }
            return sensorsUpdateMessage;
        }

        public SensorHistoryListMessage GetSensorHistory(User user, string name, string path, string product,
            long n = -1)
        {
            SensorHistoryListMessage sensorsUpdate = new SensorHistoryListMessage();
            List<SensorDataObject> dataList = DatabaseClass.Instance.GetSensorDataHistory(product, path, n);
            //_logger.Info($"GetSensorHistory: {dataList.Count} history items found for sensor {getMessage.Path} at {DateTime.Now:F}");
            dataList.Sort((a, b) => a.TimeCollected.CompareTo(b.TimeCollected));
            if (n != -1)
            {
                dataList = dataList.TakeLast((int)n).ToList();
            }
            sensorsUpdate.Sensors.AddRange(dataList.Select(Converter.Convert));
            return sensorsUpdate;
        }

        public ProductsListMessage GetProductsList(User user)
        {
            var products = _productManager.Products;

            ProductsListMessage message = new ProductsListMessage();
            message.Products.AddRange(products.Select(Converter.Convert));
            return message;
        }

        public AddProductResultMessage AddProduct(User user, string productName)
        {
            AddProductResultMessage result = new AddProductResultMessage();
            try
            {
                _productManager.AddProduct(productName);
                Product product = _productManager.GetProductByName(productName);

                result.Result = true;
                result.ProductData = Converter.Convert(product);
            }
            catch (Exception e)
            {
                result.Result = false;
                result.Error = e.Message;
                _logger.Error(e, $"Failed to add new product name = {productName}, user = {user.UserName}");
            }

            return result;
        }

        public RemoveProductResultMessage RemoveProduct(User user, string productName)
        {
            RemoveProductResultMessage result = new RemoveProductResultMessage();
            try
            {
                result.ProductData = Converter.Convert(_productManager.GetProductByName(productName));
                _productManager.RemoveProduct(productName);
                result.Result = true;
            }
            catch (Exception e)
            {
                result.Result = false;
                result.Error = e.Message;
                _logger.Error(e, $"Failed to remove product name = {productName}, user = {user.UserName}");
            }

            return result;
        }

        public SignedCertificateMessage SignClientCertificate(User user, CertificateSignRequestMessage request)
        {
            var rsa = RSA.Create(Converter.Convert(request.RSAParameters));

            X509Certificate2 clientCert =
                CertificatesProcessor.CreateAndSignCertificate(request.Subject, rsa, Config.CACertificate);

            string fileName = $"{request.CommonName}.crt";
            _certificateManager.InstallClientCertificate(clientCert);
            _certificateManager.SaveClientCertificate(clientCert, fileName);
            _userManager.AddNewUser(request.CommonName, clientCert.Thumbprint, fileName);
            return Converter.Convert(clientCert, Config.CACertificate);
        }

        public ClientVersionMessage GetLastAvailableClientVersion()
        {
            return Converter.Convert(Config.LastAvailableClientVersion);
        }

        #region Sub-methods


        #endregion
    }
}
