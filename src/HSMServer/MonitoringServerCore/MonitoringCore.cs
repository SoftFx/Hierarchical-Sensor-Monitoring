using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMCommon.Certificates;
using HSMCommon.Model;
using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Authentication;
using HSMServer.Cache;
using HSMServer.Configuration;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using HSMServer.Model;
using HSMServer.Products;
using Microsoft.Extensions.Logging;
using RSAParameters = System.Security.Cryptography.RSAParameters;

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

        private readonly IDatabaseClass _database;
        private readonly IBarSensorsStorage _barsStorage;
        private readonly IMonitoringQueueManager _queueManager;
        private readonly IUserManager _userManager;
        private readonly CertificateManager _certificateManager;
        private readonly IProductManager _productManager;
        private readonly IConfigurationProvider _configurationProvider;
        private readonly ILogger<MonitoringCore> _logger;
        private readonly IValuesCache _valuesCache;
        private readonly IConverter _converter;

        public MonitoringCore(IDatabaseClass database, IUserManager userManager, IBarSensorsStorage barsStorage,
            IProductManager productManager, IConfigurationProvider configurationProvider, IValuesCache valuesVCache,
            IConverter converter, ILogger<MonitoringCore> logger)
        {
            _logger = logger;
            _database = database;
            _barsStorage = barsStorage;
            _barsStorage.IncompleteBarOutdated += BarsStorage_IncompleteBarOutdated;
            _certificateManager = new CertificateManager();
            _userManager = userManager;
            _queueManager = new MonitoringQueueManager();
            _productManager = productManager;
            _configurationProvider = configurationProvider;
            _valuesCache = valuesVCache;
            _converter = converter;
            FillValuesCache();
            _logger.LogInformation("Monitoring core initialized");
        }

        private void FillValuesCache()
        {
            var productsList = _productManager.Products;
            foreach (var product in productsList)
            {
                var sensors = _productManager.GetProductSensors(product.Name);
                foreach (var sensor in sensors)
                {
                    var lastVal = _database.GetLastSensorValue(product.Name, sensor.Path);
                    if (lastVal != null)
                    {
                        _valuesCache.AddValue(product.Name, _converter.Convert(lastVal, sensor, product.Name));
                    }
                }
            }
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
                    SensorDataObject obj = _converter.ConvertToDatabase(typedValue, e.TimeCollected);
                    SaveSensorValue(obj, e.ProductName);
                    break;
                }
                case SensorType.DoubleBarSensor:
                {
                    var typedValue = e.Value as DoubleBarSensorValue;
                    typedValue.EndTime = DateTime.Now;
                    SensorDataObject obj = _converter.ConvertToDatabase(typedValue, e.TimeCollected);
                    SaveSensorValue(obj, e.ProductName);
                    break;
                }
            }
        }
        /// <summary>
        /// Simply save the given sensorValue
        /// </summary>
        /// <param name="dataObject"></param>
        /// <param name="productName"></param>
        private void SaveSensorValue(SensorDataObject dataObject, string productName)
        {
            //_productManager.AddSensorIfNotRegistered(productName, dataObject.Path);
            Task.Run(() => _database.WriteSensorData(dataObject, productName));
        }

        /// <summary>
        /// Use this method for sensors, for which only last value is stored
        /// </summary>
        /// <param name="dataObject"></param>
        /// <param name="productName"></param>
        private void SaveOneValueSensorValue(SensorDataObject dataObject, string productName)
        {
            Task.Run(() => _database.WriteOneValueSensorData(dataObject, productName));
        }

        public void AddSensorsValues(IEnumerable<CommonSensorValue> values)
        {
            var commonSensorValues = values.ToList();
            foreach (var value in commonSensorValues)
            {
                if (value == null)
                {
                    _logger.LogWarning("Received null value in list!");
                    continue;
                }
                switch (value.SensorType)
                {
                    case SensorType.IntegerBarSensor:
                    {
                        var typedValue = _converter.GetIntBarSensorValue(value.TypedValue);
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.DoubleBarSensor:
                    {
                        var typedValue = _converter.GetDoubleBarSensorValue(value.TypedValue);
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.DoubleSensor:
                    {
                        var typedValue = _converter.GetDoubleSensorValue(value.TypedValue);
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.IntSensor:
                    {
                        var typedValue = _converter.GetIntSensorValue(value.TypedValue);
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.BooleanSensor:
                    {
                        var typedValue = _converter.GetBoolSensorValue(value.TypedValue);
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.StringSensor:
                    {
                        var typedValue = _converter.GetStringSensorValue(value.TypedValue);
                        AddSensorValue(typedValue);
                        break;
                    }
                }
            }
        }

        public void AddSensorValue(BoolSensorValue value)
        {
            try
            {
                string productName = _productManager.GetProductNameByKey(value.Key);

                bool isNew = false;
                if (!_productManager.IsSensorRegistered(productName, value.Path))
                {
                    isNew = true;
                    _productManager.AddSensor(productName, value);
                }
                DateTime timeCollected = DateTime.Now;
                SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                _queueManager.AddSensorData(updateMessage);
                _valuesCache.AddValue(productName, updateMessage);

                SensorDataObject dataObject = _converter.ConvertToDatabase(value, timeCollected);
                Task.Run(() => SaveSensorValue(dataObject, productName));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
        }
        public void AddSensorValue(IntSensorValue value)
        {
            try
            {
                string productName = _productManager.GetProductNameByKey(value.Key);

                bool isNew = false;
                if (!_productManager.IsSensorRegistered(productName, value.Path))
                {
                    isNew = true;
                    _productManager.AddSensor(productName, value);
                }
                DateTime timeCollected = DateTime.Now;
                SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                _queueManager.AddSensorData(updateMessage);
                _valuesCache.AddValue(productName, updateMessage);

                SensorDataObject dataObject = _converter.ConvertToDatabase(value, timeCollected);
                Task.Run(() => SaveSensorValue(dataObject, productName));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
        }

        public void AddSensorValue(DoubleSensorValue value)
        {
            try
            {
                string productName = _productManager.GetProductNameByKey(value.Key);
                bool isNew = false;
                if (!_productManager.IsSensorRegistered(productName, value.Path))
                {
                    isNew = true;
                    _productManager.AddSensor(productName, value);
                }
                DateTime timeCollected = DateTime.Now;
                SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                _queueManager.AddSensorData(updateMessage);
                _valuesCache.AddValue(productName, updateMessage);

                SensorDataObject dataObject = _converter.ConvertToDatabase(value, timeCollected);
                Task.Run(() => SaveSensorValue(dataObject, productName));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
        }

        public void AddSensorValue(StringSensorValue value)
        {
            try
            {
                string productName = _productManager.GetProductNameByKey(value.Key);
                bool isNew = false;
                if (!_productManager.IsSensorRegistered(productName, value.Path))
                {
                    isNew = true;
                    _productManager.AddSensor(productName, value);
                }
                DateTime timeCollected = DateTime.Now;
                SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                _queueManager.AddSensorData(updateMessage);
                _valuesCache.AddValue(productName, updateMessage);

                SensorDataObject dataObject = _converter.ConvertToDatabase(value, timeCollected);
                Task.Run(() => SaveSensorValue(dataObject, productName));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
        }

        public void AddSensorValue(FileSensorValue value)
        {
            try
            {
                string productName = _productManager.GetProductNameByKey(value.Key);
                bool isNew = false;
                if (!_productManager.IsSensorRegistered(productName, value.Path))
                {
                    isNew = true;
                    _productManager.AddSensor(productName, value);
                }
                DateTime timeCollected = DateTime.Now;
                SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                _queueManager.AddSensorData(updateMessage);
                _valuesCache.AddValue(productName, updateMessage);

                SensorDataObject dataObject = _converter.ConvertToDatabase(value, timeCollected);
                Task.Run(() => SaveSensorValue(dataObject, productName));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
        }

        public void AddSensorValue(FileSensorBytesValue value)
        {
            try
            {
                string productName = _productManager.GetProductNameByKey(value.Key);
                bool isNew = false;
                if (!_productManager.IsSensorRegistered(productName, value.Path))
                {
                    isNew = true;
                    _productManager.AddSensor(productName, value);
                }
                DateTime timeCollected = DateTime.Now;
                SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                _queueManager.AddSensorData(updateMessage);
                _valuesCache.AddValue(productName, updateMessage);

                SensorDataObject dataObject = _converter.ConvertToDatabase(value, timeCollected);
                Task.Run(() => SaveSensorValue(dataObject, productName));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
        }
        public void AddSensorValue(IntBarSensorValue value)
        {
            try
            {
                string productName = _productManager.GetProductNameByKey(value.Key);
                bool isNew = false;
                if (!_productManager.IsSensorRegistered(productName, value.Path))
                {
                    isNew = true;
                    _productManager.AddSensor(productName, value);
                }
                DateTime timeCollected = DateTime.Now;
                SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                _queueManager.AddSensorData(updateMessage);
                _valuesCache.AddValue(productName, updateMessage);

                //Skip 
                if (value.EndTime == DateTime.MinValue)
                {
                    _barsStorage.Add(value, updateMessage.Product, timeCollected);
                    return;
                }
                
                _barsStorage.Remove(productName, value.Path);
                SensorDataObject dataObject = _converter.ConvertToDatabase(value, timeCollected);
                Task.Run(() => SaveSensorValue(dataObject, productName));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
        }

        public void AddSensorValue(DoubleBarSensorValue value)
        {
            try
            {
                string productName = _productManager.GetProductNameByKey(value.Key);
                bool isNew = false;
                if (!_productManager.IsSensorRegistered(productName, value.Path))
                {
                    isNew = true;
                    _productManager.AddSensor(productName, value);
                }
                DateTime timeCollected = DateTime.Now;
                SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                _queueManager.AddSensorData(updateMessage);
                _valuesCache.AddValue(productName, updateMessage);

                if (value.EndTime == DateTime.MinValue)
                {
                    _barsStorage.Add(value, updateMessage.Product, timeCollected);
                    return;
                }

                _barsStorage.Remove(productName, value.Path);
                SensorDataObject dataObject = _converter.ConvertToDatabase(value, timeCollected);
                Task.Run(() => SaveSensorValue(dataObject, productName));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
        }

        #endregion

        public List<SensorData> GetSensorUpdates(User user)
        {
            return _queueManager.GetUserUpdates(user);
        }

        public List<SensorData> GetSensorsTree(User user)
        {
            if (!_queueManager.IsUserRegistered(user))
            {
                _queueManager.AddUserSession(user);
            }

            List<SensorData> result = new List<SensorData>();
            var productsList = _productManager.Products;
            //Show available products only
            if (!UserRoleHelper.IsAllProductsTreeAllowed(user.Role))
                productsList = productsList.Where(p => 
                ProductRoleHelper.IsAvailable(p.Key, user.ProductsRoles)).ToList();
            
            //foreach (var product in productsList)
            //{
                //result.AddRange();
                //var sensorsList = _productManager.GetProductSensors(product.Name);
                //foreach (var sensor in sensorsList)
                //{
                //    var cachedVal = _valuesCache.GetValue(product.Name, sensor.Path);
                //    if (cachedVal != null)
                //    {
                //        result.Add(cachedVal);
                //        continue;
                //    }
                //    var lastVal = _database.GetLastSensorValue(product.Name, sensor.Path);
                //    if (lastVal != null)
                //    {
                //        result.Add(_converter.Convert(lastVal, product.Name));
                //    }
                //}
            //}
            result.AddRange(_valuesCache.GetValues(productsList.Select(p => p.Name).ToList()));

            return result;
        }

        public List<SensorHistoryData> GetSensorHistory(User user, GetSensorHistoryModel model)
        {
            return GetSensorHistory(user, model.Path, model.Product, model.TotalCount);
        }
        public List<SensorHistoryData> GetSensorHistory(User user, string path, string product, long n = -1)
        {
            List<SensorHistoryData> historyList = new List<SensorHistoryData>();
            List<SensorDataObject> dataList = _database.GetSensorDataHistory(product, path, n);
            //_logger.Info($"GetSensorHistory: {dataList.Count} history items found for sensor {getMessage.Path} at {DateTime.Now:F}");
            dataList.Sort((a, b) => a.TimeCollected.CompareTo(b.TimeCollected));
            if (n != -1)
            {
                dataList = dataList.TakeLast((int)n).ToList();
            }

            var finalList = dataList.Select(_converter.Convert).ToList();
            var lastValue = _barsStorage.GetLastValue(product, path);
            if (lastValue != null)
            {
                finalList.Add(_converter.Convert(lastValue));
            }

            historyList.AddRange(n == -1 ? finalList : finalList.TakeLast((int) n));
            return historyList;
        }

        public string GetFileSensorValue(User user, string product, string path)
        {
            List<SensorDataObject> historyList = _database.GetSensorDataHistory(product, path, 1);
            if (historyList.Count < 1)
            {
                return string.Empty;
            }

            var typedData = JsonSerializer.Deserialize<FileSensorData>(historyList[0].TypedData);
            if (typedData != null)
            {
                return typedData.FileContent;
            }

            
            return string.Empty;
        }

        public byte[] GetFileSensorValueBytes(User user, string product, string path)
        {
            List<SensorDataObject> historyList = _database.GetSensorDataHistory(product, path, 1);
            if (historyList.Count < 1)
            {
                return new byte[1];
            }

            try
            {
                var typedData2 = JsonSerializer.Deserialize<FileSensorBytesData>(historyList[0].TypedData);
                return typedData2?.FileContent;
            }
            catch { }
            

            var typedData = JsonSerializer.Deserialize<FileSensorData>(historyList[0].TypedData);
            if (typedData != null)
            {
                return Encoding.Default.GetBytes(typedData.FileContent);
            }
            
            return new byte[1];
        }
        public string GetFileSensorValueExtension(User user, string product, string path)
        {
            List<SensorDataObject> historyList = _database.GetSensorDataHistory(product, path, 1);
            if (historyList.Count < 1)
            {
                return string.Empty;
            }
            var typedData = JsonSerializer.Deserialize<FileSensorData>(historyList[0].TypedData);
            if (typedData != null)
            {
                return typedData.Extension;
            }
            var typedData2 = JsonSerializer.Deserialize<FileSensorBytesData>(historyList[0].TypedData);
            if (typedData2 != null)
            {
                return typedData2.Extension;
            }

            return string.Empty;
        }

        public Product GetProduct(string productKey)
        {
            var product = _productManager.Products.FirstOrDefault(x => x.Key.Equals(productKey));
            if (product == null)
                _logger.LogError($"Failed to find the product with key {productKey}");
            return product == null ? null : new Product(product);
        }

        public List<Product> GetProducts(User user)
        {
            if (user.ProductsRoles == null || !user.ProductsRoles.Any()) return null;

            return _productManager.Products.Where(p => 
                ProductRoleHelper.IsAvailable(p.Key, user.ProductsRoles)).ToList();
        }

        public List<Product> GetAllProducts()
        {
            return _productManager.Products;
        }

        public bool AddProduct(User user, string productName, out Product product, out string error)
        {
            product = default(Product);
            error = string.Empty;
            bool result;
            try
            {
                _productManager.AddProduct(productName);
                product = _productManager.GetProductByName(productName);
                result = true;
            }
            catch (Exception e)
            {
                result = false;
                error = e.Message;
                _logger.LogError(e, $"Failed to add new product name = {productName}, user = {user.UserName}");
            }

            return result;
        }

        public bool RemoveProduct(User user, string productName, out Product product, out string error)
        {
            product = default(Product);
            error = string.Empty;
            bool result;
            try
            {
                product = _productManager.GetProductByName(productName);
                _productManager.RemoveProduct(productName);
                result = true;
            }
            catch (Exception e)
            {
                result = false;
                error = e.Message;
                _logger.LogError(e, $"Failed to remove product name = {productName}, user = {user.UserName}");
            }

            return result;
        }

        public bool RemoveProduct(string productKey, out string error)
        {           
            bool result;
            error = string.Empty;
            string productName = string.Empty;
            try
            {
                productName = _productManager.GetProductNameByKey(productKey);
                _productManager.RemoveProduct(productName);
                result = true;
            }
            catch(Exception ex)
            {
                result = false;
                error = ex.Message;
                _logger.LogError(ex, $"Failed to remove product name = {productName}");
            }
            return result;
        }

        public void UpdateProduct(User user, Product product)
        {
            _productManager.UpdateProduct(product);
        }

        public (X509Certificate2, X509Certificate2) SignClientCertificate(User user, string subject, string commonName,
            RSAParameters rsaParameters)
        {
            (X509Certificate2, X509Certificate2) result;
            var rsa = RSA.Create(rsaParameters);

            X509Certificate2 clientCert =
                CertificatesProcessor.CreateAndSignCertificate(subject, rsa, CertificatesConfig.CACertificate);

            string fileName = $"{commonName}.crt";
            _certificateManager.InstallClientCertificate(clientCert);
            _certificateManager.SaveClientCertificate(clientCert, fileName);
            _userManager.AddUser(commonName, clientCert.Thumbprint, fileName,
                HashComputer.ComputePasswordHash(commonName), UserRoleEnum.SystemAdmin);
            result.Item1 = clientCert;
            result.Item2 = CertificatesConfig.CACertificate;
            return result;
        }

        public ClientVersionModel GetLastAvailableClientVersion()
        {
            return _configurationProvider.ClientVersion;
        }

        #region Sub-methods


        #endregion

        public void Dispose()
        {
            _barsStorage?.Dispose();
            _database?.Dispose();
        }
    }
}
