using HSMCommon;
using HSMCommon.Certificates;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Authentication;
using HSMServer.Core.Authentication.UserObserver;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Converters;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringCoreInterface;
using HSMServer.Core.Products;
using HSMServer.Core.SensorsDataProcessor;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RSAParameters = System.Security.Cryptography.RSAParameters;
using HSMServer.Core.SensorsDataValidation;

namespace HSMServer.Core.MonitoringServerCore
{
    public class MonitoringCore : IMonitoringCore, IMonitoringDataReceiver, IProductsInterface,
        ISensorsInterface, IUserObserver, IMonitoringUpdatesReceiver
    {
        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly IBarSensorsStorage _barsStorage;
        private readonly IMonitoringQueueManager _queueManager;
        private readonly IUserManager _userManager;
        private readonly CertificateManager _certificateManager;
        private readonly IProductManager _productManager;
        private readonly IConfigurationProvider _configurationProvider;
        private readonly ISensorsProcessor _sensorsProcessor;
        private readonly ILogger<MonitoringCore> _logger;
        private readonly IValuesCache _valuesCache;

        public MonitoringCore(IDatabaseAdapter databaseAdapter, IUserManager userManager, IBarSensorsStorage barsStorage,
            IProductManager productManager, ISensorsProcessor sensorsProcessor, IConfigurationProvider configurationProvider, 
            IValuesCache valuesVCache, ILogger<MonitoringCore> logger)
        {
            _logger = logger;
            _databaseAdapter = databaseAdapter;
            _barsStorage = barsStorage;
            _barsStorage.IncompleteBarOutdated += BarsStorage_IncompleteBarOutdated;
            _certificateManager = new CertificateManager();
            _userManager = userManager;
            userManager.AddObserver(this);
            _queueManager = new MonitoringQueueManager(userManager);
            _productManager = productManager;
            _configurationProvider = configurationProvider;
            _sensorsProcessor = sensorsProcessor;
            _valuesCache = valuesVCache;
            //MigrateSensorsValuesToNewDatabase();
            Thread.Sleep(5000);
            FillValuesCache();
            _logger.LogInformation("Monitoring core initialized");

            SensorDataValidationExtensions.Initialize(configurationProvider);
        }

        //private void MigrateSensorsValuesToNewDatabase()
        //{
        //    foreach (var product in _productManager.Products)
        //    {
        //        var sensors = _productManager.GetProductSensors(product.Name);
        //        foreach (var sensor in sensors)
        //        {
        //            var history = _databaseAdapter.GetAllSensorDataOld(product.Name, sensor.Path);
        //            foreach (var historyItem in history)
        //            {
        //                _databaseAdapter.PutSensorData(historyItem, product.Name);
        //            }
        //        }
        //    }
        //}

        private void FillValuesCache()
        {
            var productsList = _productManager.Products;
            foreach (var product in productsList)
            {
                var sensors = _productManager.GetProductSensors(product.Name);
                foreach (var sensor in sensors)
                {
                    //var lastVal = _databaseAdapter.GetLastSensorValueOld(product.Name, sensor.Path);
                    var lastVal = _databaseAdapter.GetLastSensorValue(product.Name, sensor.Path);
                    if (lastVal != null)
                    {
                        _valuesCache.AddValue(product.Name, lastVal.Convert(sensor, product.Name));
                    }
                }
            }
        }

        #region Sensor saving
        private void BarsStorage_IncompleteBarOutdated(object sender, ExtendedBarSensorData e)
        {
            ProcessExtendedBarData(e);
        }

        private void ProcessExtendedBarData(ExtendedBarSensorData extendedData)
        {
            switch (extendedData.ValueType)
            {
                case SensorType.IntegerBarSensor:
                {
                    var typedValue = extendedData.Value as IntBarSensorValue;
                    typedValue.EndTime = DateTime.UtcNow;
                    SensorDataEntity obj = typedValue.Convert(extendedData.TimeCollected);
                    SaveSensorValue(obj, extendedData.ProductName);
                    break;
                }
                case SensorType.DoubleBarSensor:
                {
                    var typedValue = extendedData.Value as DoubleBarSensorValue;
                    typedValue.EndTime = DateTime.UtcNow;
                    SensorDataEntity obj = typedValue.Convert(extendedData.TimeCollected);
                    SaveSensorValue(obj, extendedData.ProductName);
                    break;
                }
            }
        }
        /// <summary>
        /// Simply save the given sensorValue
        /// </summary>
        /// <param name="dataObject"></param>
        /// <param name="productName"></param>
        private void SaveSensorValue(SensorDataEntity dataObject, string productName)
        {
            //await Task.Run(() => _databaseAdapter.PutSensorData(dataObject, productName));
            _databaseAdapter.PutSensorData(dataObject, productName);
        }

        /// <summary>
        /// Use this method for sensors, for which only last value is stored
        /// </summary>
        /// <param name="dataObject"></param>
        /// <param name="productName"></param>
        private void SaveOneValueSensorValue(SensorDataEntity dataObject, string productName)
        {
            //Task.Run(() => _databaseAdapter.PutOneValueSensorDataOld(dataObject, productName));
            Task.Run(() => _databaseAdapter.PutSensorData(dataObject, productName));
        }

        public void AddSensorsValues(List<CommonSensorValue> values)
        {
            foreach (var value in values)
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
                        var typedValue = value.Convert<IntBarSensorValue>();
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.DoubleBarSensor:
                    {
                        var typedValue = value.Convert<DoubleBarSensorValue>();
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.DoubleSensor:
                    {
                        var typedValue = value.Convert<DoubleSensorValue>();
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.IntSensor:
                    {
                        var typedValue = value.Convert<IntSensorValue>();
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.BooleanSensor:
                    {
                        var typedValue = value.Convert<BoolSensorValue>();
                        AddSensorValue(typedValue);
                        break;
                    }
                    case SensorType.StringSensor:
                    {
                        var typedValue = value.Convert<StringSensorValue>();
                        AddSensorValue(typedValue);
                        break;
                    }
                }
            }
        }

        #region Typed Sensors from UnitedSensorValue

        public void AddSensorsValues(List<UnitedSensorValue> values)
        {
            foreach (var value in values)
            {
                try
                {
                    AddUnitedValue(value);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Failed to add data for {value?.Path}");
                }
                
            }
        }
        private void AddUnitedValue(UnitedSensorValue value)
        {
            try
            {
                //string productName = _productManager.GetProductNameByKey(value.Key);
                //TransactionType type = TransactionType.Unknown;
                //if (!_productManager.IsSensorRegistered(productName, value.Path))
                //{
                //    _productManager.AddSensor(productName, value);
                //    type = TransactionType.Add;
                //}
                //DateTime timeCollected = DateTime.UtcNow;
                //SensorData updateMessage = _converter.ConvertUnitedValue(value, productName, timeCollected, type);
                //_queueManager.AddSensorData(updateMessage);
                //_valuesCache.AddValue(productName, updateMessage);
                DateTime timeCollected = DateTime.UtcNow;
                var processingResult = _sensorsProcessor.ProcessUnitedData(value, timeCollected, out var processedData, out var error);
                if (processingResult == ValidationResult.Failed)
                {
                    _logger.LogError($"Sensor data processing error. Sensor: '{value?.Path}', error: '{error}'");
                    return;
                }

                if (processingResult == ValidationResult.OkWithError)
                {
                    _logger.LogWarning($"Sensor data processed with non-critical errors. Sensor: '{value?.Path}', error: '{error}'");
                }

                _queueManager.AddSensorData(processedData);
                _valuesCache.AddValue(processedData.Product, processedData);
                
                bool isToDB = true;
                if (value.IsBarSensor())
                {
                    isToDB = ProcessBarSensor(value, timeCollected, processedData.Product);
                }

                if (!isToDB)
                    return;

                SensorDataEntity dataObject = value.Convert(timeCollected);
                SaveSensorValue(dataObject, processedData.Product);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor {value?.Path}");
            }
        }

        private bool ProcessBarSensor(UnitedSensorValue value, DateTime timeCollected, string productName)
        {
            try
            {
                var bar = value.Convert();

                if (bar.EndTime != DateTime.MinValue)
                {
                    _barsStorage.Remove(productName, value.Path);
                    return true;
                }

                if (value.Type == SensorType.IntegerBarSensor)
                {
                    var intBar = (IntBarSensorValue) bar;
                    _barsStorage.Add(intBar, productName, timeCollected);
                    return false;
                }

                var doubleBar = (DoubleBarSensorValue) bar;
                _barsStorage.Add(doubleBar, productName, timeCollected);
                return false;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to process bar sensor {value.Path}");
            }

            return true;
        }
        #endregion

        public void RemoveSensor(string product, string key, string path)
        {
            try
            {
                DateTime timeCollected = DateTime.UtcNow;

                SensorData updateMessage = new SensorData();
                updateMessage.Product = product;
                updateMessage.Key = key;
                updateMessage.Path = path;
                updateMessage.TransactionType = TransactionType.Delete;
                updateMessage.Time = timeCollected;

                _queueManager.AddSensorData(updateMessage);
                _productManager.RemoveSensor(product, path);
                _valuesCache.RemoveSensorValue(product, path);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to remove value for sensor '{product}/{product}'");
            }
        }

        public void RemoveSensors(string product, string key, IEnumerable<string> paths)
        {
            if (paths != null && paths.Any())
                foreach (var path in paths)
                    RemoveSensor(product, key, path);
        }

        private TransactionType AddSensorIfNotRegisteredAndGetTransactionType(string productName, SensorValueBase value)
        {
            var transactionType = TransactionType.Update;

            if (!_productManager.IsSensorRegistered(productName, value.Path))
            {
                _productManager.AddSensor(productName, value);
                transactionType = TransactionType.Add;
            }

            return transactionType;
        }

        private SensorData GetSensorData(SensorValueBase value, DateTime timeCollected, ValidationResult<SensorValueBase> validationResult)
        {
            var productName = _productManager.GetProductNameByKey(value.Key);
            var transactionType = AddSensorIfNotRegisteredAndGetTransactionType(productName, value);

            var sensorData = value.Convert(productName, timeCollected, transactionType);
            sensorData.Status = validationResult.SensorStatus > sensorData.Status ? validationResult.SensorStatus : sensorData.Status;
            sensorData.ValidationError = validationResult.Error;

            return sensorData;
        }

        public void AddSensorValue(BoolSensorValue value)
        {
            try
            {
                DateTime timeCollected = DateTime.UtcNow;

                var validationResult = value.Validate();

                if (validationResult.ResultType == ResultType.Failed)
                {
                    _logger.LogError($"Sensor data validation error(s). Sensor: '{value?.Path}', error(s): '{validationResult.Error}'");
                    return;
                }
                else if (validationResult.ResultType == ResultType.Warning)
                    _logger.LogWarning($"Sensor data validated with non-critical error(s). Sensor: '{value?.Path}', error(s): '{validationResult.Error}'");               

                var processedData = GetSensorData(value, timeCollected, validationResult);

                _queueManager.AddSensorData(processedData);
                _valuesCache.AddValue(processedData.Product, processedData);
                SensorDataEntity databaseObj = value.Convert(timeCollected, processedData.Status);
                SaveSensorValue(databaseObj, processedData.Product);
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
                DateTime timeCollected = DateTime.UtcNow;
                var processingResult = _sensorsProcessor.ProcessData(value, timeCollected, out var processedData, out var error);
                if (processingResult == ValidationResult.Failed)
                {
                    _logger.LogError($"Sensor data processing error. Sensor: '{value?.Path}', error: '{error}'");
                    return;
                }

                if (processingResult == ValidationResult.OkWithError)
                {
                    _logger.LogWarning($"Sensor data processed with non-critical errors. Sensor: '{value?.Path}', error: '{error}'");
                }

                _queueManager.AddSensorData(processedData);
                _valuesCache.AddValue(processedData.Product, processedData);
                SensorDataEntity databaseObj = value.Convert(timeCollected, processedData.Status);
                SaveSensorValue(databaseObj, processedData.Product);
                //string productName = _productManager.GetProductNameByKey(value.Key);

                //bool isNew = false;
                //if (!_productManager.IsSensorRegistered(productName, value.Path))
                //{
                //    isNew = true;
                //    _productManager.AddSensor(productName, value);
                //}
                //DateTime timeCollected = DateTime.UtcNow;
                //SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                //_queueManager.AddSensorData(updateMessage);
                //_valuesCache.AddValue(productName, updateMessage);

                //SensorDataEntity dataObject = _converter.ConvertToDatabase(value, timeCollected);
                //Task.Run(() => SaveSensorValue(dataObject, productName));
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
                DateTime timeCollected = DateTime.UtcNow;
                var processingResult = _sensorsProcessor.ProcessData(value, timeCollected, out var processedData, out var error);
                if (processingResult == ValidationResult.Failed)
                {
                    _logger.LogError($"Sensor data processing error. Sensor: '{value?.Path}', error: '{error}'");
                    return;
                }

                if (processingResult == ValidationResult.OkWithError)
                {
                    _logger.LogWarning($"Sensor data processed with non-critical errors. Sensor: '{value?.Path}', error: '{error}'");
                }

                _queueManager.AddSensorData(processedData);
                _valuesCache.AddValue(processedData.Product, processedData);
                SensorDataEntity databaseObj = value.Convert(timeCollected, processedData.Status);
                SaveSensorValue(databaseObj, processedData.Product);
                //string productName = _productManager.GetProductNameByKey(value.Key);
                //bool isNew = false;
                //if (!_productManager.IsSensorRegistered(productName, value.Path))
                //{
                //    isNew = true;
                //    _productManager.AddSensor(productName, value);
                //}
                //DateTime timeCollected = DateTime.UtcNow;
                //SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                //_queueManager.AddSensorData(updateMessage);
                //_valuesCache.AddValue(productName, updateMessage);

                //SensorDataEntity dataObject = _converter.ConvertToDatabase(value, timeCollected);
                //Task.Run(() => SaveSensorValue(dataObject, productName));
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
                DateTime timeCollected = DateTime.UtcNow;
                var processingResult = _sensorsProcessor.ProcessData(value, timeCollected, out var processedData, out var error);
                if (processingResult == ValidationResult.Failed)
                {
                    _logger.LogError($"Sensor data processing error. Sensor: '{value?.Path}', error: '{error}'");
                    return;
                }

                if (processingResult == ValidationResult.OkWithError)
                {
                    _logger.LogWarning($"Sensor data processed with non-critical errors. Sensor: '{value?.Path}', error: '{error}'");
                }

                _queueManager.AddSensorData(processedData);
                _valuesCache.AddValue(processedData.Product, processedData);
                SensorDataEntity databaseObj = value.Convert(timeCollected, processedData.Status);
                SaveSensorValue(databaseObj, processedData.Product);
                //string productName = _productManager.GetProductNameByKey(value.Key);
                //bool isNew = false;
                //if (!_productManager.IsSensorRegistered(productName, value.Path))
                //{
                //    isNew = true;
                //    _productManager.AddSensor(productName, value);
                //}
                //DateTime timeCollected = DateTime.UtcNow;
                //SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                //_queueManager.AddSensorData(updateMessage);
                //_valuesCache.AddValue(productName, updateMessage);

                //SensorDataEntity dataObject = _converter.ConvertToDatabase(value, timeCollected);
                //Task.Run(() => SaveSensorValue(dataObject, productName));
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
                DateTime timeCollected = DateTime.UtcNow;
                var processingResult = _sensorsProcessor.ProcessData(value, timeCollected, out var processedData, out var error);
                if (processingResult == ValidationResult.Failed)
                {
                    _logger.LogError($"Sensor data processing error. Sensor: '{value?.Path}', error: '{error}'");
                    return;
                }

                if (processingResult == ValidationResult.OkWithError)
                {
                    _logger.LogWarning($"Sensor data processed with non-critical errors. Sensor: '{value?.Path}', error: '{error}'");
                }

                _queueManager.AddSensorData(processedData);
                _valuesCache.AddValue(processedData.Product, processedData);
                SensorDataEntity databaseObj = value.Convert(timeCollected, processedData.Status);
                SaveSensorValue(databaseObj, processedData.Product);
                //string productName = _productManager.GetProductNameByKey(value.Key);
                //bool isNew = false;
                //if (!_productManager.IsSensorRegistered(productName, value.Path))
                //{
                //    isNew = true;
                //    _productManager.AddSensor(productName, value);
                //}
                //DateTime timeCollected = DateTime.UtcNow;
                //SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                //_queueManager.AddSensorData(updateMessage);
                //_valuesCache.AddValue(productName, updateMessage);

                //SensorDataEntity dataObject = _converter.ConvertToDatabase(value, timeCollected);
                //Task.Run(() => SaveSensorValue(dataObject, productName));
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
                DateTime timeCollected = DateTime.UtcNow;
                var processingResult = _sensorsProcessor.ProcessData(value, timeCollected, out var processedData, out var error);
                if (processingResult == ValidationResult.Failed)
                {
                    _logger.LogError($"Sensor data processing error. Sensor: '{value?.Path}', error: '{error}'");
                    return;
                }

                if (processingResult == ValidationResult.OkWithError)
                {
                    _logger.LogWarning($"Sensor data processed with non-critical errors. Sensor: '{value?.Path}', error: '{error}'");
                }

                _queueManager.AddSensorData(processedData);
                _valuesCache.AddValue(processedData.Product, processedData);
                SensorDataEntity databaseObj = value.Convert(timeCollected, processedData.Status);
                SaveSensorValue(databaseObj, processedData.Product);
                //string productName = _productManager.GetProductNameByKey(value.Key);
                //bool isNew = false;
                //if (!_productManager.IsSensorRegistered(productName, value.Path))
                //{
                //    isNew = true;
                //    _productManager.AddSensor(productName, value);
                //}
                //DateTime timeCollected = DateTime.UtcNow;
                //SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                //_queueManager.AddSensorData(updateMessage);
                //_valuesCache.AddValue(productName, updateMessage);

                //SensorDataEntity dataObject = _converter.ConvertToDatabase(value, timeCollected);
                //Task.Run(() => SaveSensorValue(dataObject, productName));
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
                DateTime timeCollected = DateTime.UtcNow;
                var processingResult = _sensorsProcessor.ProcessData(value, timeCollected, out var processedData, out var error);
                if (processingResult == ValidationResult.Failed)
                {
                    _logger.LogError($"Sensor data processing error. Sensor: '{value?.Path}', error: '{error}'");
                    return;
                }

                if (processingResult == ValidationResult.OkWithError)
                {
                    _logger.LogWarning($"Sensor data processed with non-critical errors. Sensor: '{value?.Path}', error: '{error}'");
                }

                _queueManager.AddSensorData(processedData);
                _valuesCache.AddValue(processedData.Product, processedData);
                //string productName = _productManager.GetProductNameByKey(value.Key);
                //bool isNew = false;
                //if (!_productManager.IsSensorRegistered(productName, value.Path))
                //{
                //    isNew = true;
                //    _productManager.AddSensor(productName, value);
                //}
                //DateTime timeCollected = DateTime.UtcNow;
                //SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                //_queueManager.AddSensorData(updateMessage);
                //_valuesCache.AddValue(productName, updateMessage);

                //Skip 
                if (value.EndTime == DateTime.MinValue)
                {
                    _barsStorage.Add(value, processedData.Product, timeCollected);
                    return;
                }
                
                _barsStorage.Remove(processedData.Product, value.Path);
                //SensorDataEntity dataObject = _converter.ConvertToDatabase(value, timeCollected);
                //Task.Run(() => SaveSensorValue(dataObject, productName));
                SensorDataEntity databaseObj = value.Convert(timeCollected, processedData.Status);
                SaveSensorValue(databaseObj, processedData.Product);
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
                DateTime timeCollected = DateTime.UtcNow;
                var processingResult = _sensorsProcessor.ProcessData(value, timeCollected, out var processedData, out var error);
                if (processingResult == ValidationResult.Failed)
                {
                    _logger.LogError($"Sensor data processing error. Sensor: '{value?.Path}', error: '{error}'");
                    return;
                }

                if (processingResult == ValidationResult.OkWithError)
                {
                    _logger.LogWarning($"Sensor data processed with non-critical errors. Sensor: '{value?.Path}', error: '{error}'");
                }

                _queueManager.AddSensorData(processedData);
                _valuesCache.AddValue(processedData.Product, processedData);
                if (value.EndTime == DateTime.MinValue)
                {
                    _barsStorage.Add(value, processedData.Product, timeCollected);
                    return;
                }

                _barsStorage.Remove(processedData.Product, value.Path);
                SensorDataEntity databaseObj = value.Convert(timeCollected, processedData.Status);
                SaveSensorValue(databaseObj, processedData.Product);
                //string productName = _productManager.GetProductNameByKey(value.Key);
                //bool isNew = false;
                //if (!_productManager.IsSensorRegistered(productName, value.Path))
                //{
                //    isNew = true;
                //    _productManager.AddSensor(productName, value);
                //}
                //DateTime timeCollected = DateTime.UtcNow;
                //SensorData updateMessage = _converter.Convert(value, productName, timeCollected, isNew ? TransactionType.Add : TransactionType.Update);
                //_queueManager.AddSensorData(updateMessage);
                //_valuesCache.AddValue(productName, updateMessage);

                //if (value.EndTime == DateTime.MinValue)
                //{
                //    _barsStorage.Add(value, updateMessage.Product, timeCollected);
                //    return;
                //}

                //_barsStorage.Remove(productName, value.Path);
                //SensorDataEntity dataObject = _converter.ConvertToDatabase(value, timeCollected);
                //Task.Run(() => SaveSensorValue(dataObject, productName));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
        }

        #endregion

        public List<SensorInfo> GetAllAvailableSensorInfos(User user)
        {
            if (user.IsAdmin)
            {
                return GetAllExistingSensorInfos();
            }

            return GetAvailableSensorInfos(user);
        }

        private List<SensorInfo> GetAllExistingSensorInfos()
        {
            return _productManager.GetAllExistingSensorInfos();
        }

        private List<SensorInfo> GetAvailableSensorInfos(User user)
        {
            List<SensorInfo> result = new List<SensorInfo>();
            foreach (var productRole in user.ProductsRoles)
            {
                if (productRole.Value == ProductRoleEnum.ProductManager)
                {
                    var product = _productManager.GetProductByKey(productRole.Key);
                    result.AddRange(_productManager.GetProductSensors(product.Name));
                }
            }

            return result;
        }
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
            if (!UserRoleHelper.IsAllProductsTreeAllowed(user))
                productsList = productsList.Where(p => 
                ProductRoleHelper.IsAvailable(p.Key, user.ProductsRoles)).ToList();
            
            result.AddRange(_valuesCache.GetValues(productsList.Select(p => p.Name).ToList()));

            return result;
        }

        #region Sensors History

        public List<SensorHistoryData> GetSensorHistory(User user, string product, string path, DateTime from, DateTime to)
        {
            var historyValues = _databaseAdapter.GetSensorHistory(product, path, 
                from, to);
            var lastValue = _barsStorage.GetLastValue(product, path);
            if (lastValue != null && lastValue.TimeCollected < to && lastValue.TimeCollected > from)
            {
                historyValues.Add(lastValue.Convert());
            }
            return historyValues;
        }

        public List<SensorHistoryData> GetAllSensorHistory(User user, string product, string path)
        {
            var allValues = _databaseAdapter.GetAllSensorHistory(product, path);
            var lastValue = _barsStorage.GetLastValue(product, path);
            if (lastValue != null)
            {
                allValues.Add(lastValue.Convert());
            }
            return allValues;
        }

        public List<SensorHistoryData> GetSensorHistory(User user, string product, string path, int n)
        {
            List<SensorHistoryData> historyList = _databaseAdapter.GetSensorHistory(product, path, n);
            var lastValue = _barsStorage.GetLastValue(product, path);
            if (lastValue != null)
            {
                historyList.Add(lastValue.Convert());
            }

            if (n != -1)
            {
                historyList = historyList.TakeLast(n).ToList();
            }

            return historyList;
        }

        public string GetFileSensorValue(User user, string product, string path)
        {
            var dataObject = _databaseAdapter.GetOneValueSensorValue(product, path);
            var typedData = JsonSerializer.Deserialize<FileSensorData>(dataObject.TypedData);
            if (typedData != null)
            {
                return typedData.FileContent;
            }


            return string.Empty;
        }

        public byte[] GetFileSensorValueBytes(User user, string product, string path)
        {
            var dataObject = _databaseAdapter.GetOneValueSensorValue(product, path);

            try
            {
                var typedData2 = JsonSerializer.Deserialize<FileSensorBytesData>(dataObject.TypedData);
                return typedData2?.FileContent;
            }
            catch { }


            var typedData = JsonSerializer.Deserialize<FileSensorData>(dataObject.TypedData);
            if (typedData != null)
            {
                return Encoding.Default.GetBytes(typedData.FileContent);
            }

            return new byte[1];
        }
        public string GetFileSensorValueExtension(User user, string product, string path)
        {
            var dataObject = _databaseAdapter.GetOneValueSensorValue(product, path);
            var typedData = JsonSerializer.Deserialize<FileSensorData>(dataObject.TypedData);
            if (typedData != null)
            {
                return typedData.Extension;
            }
            var typedData2 = JsonSerializer.Deserialize<FileSensorBytesData>(dataObject.TypedData);
            if (typedData2 != null)
            {
                return typedData2.Extension;
            }

            return string.Empty;
        }

        #endregion


        #region Product

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
            bool result = false;
            error = string.Empty;
            string productName = string.Empty;
            try
            {
                var product = _productManager.GetProductByKey(productKey);
                productName = product?.Name;
                RemoveProductFromUsers(product);
                _productManager.RemoveProduct(productName);
                _valuesCache.RemoveProduct(productName);

                DateTime timeCollected = DateTime.UtcNow;
                SensorData updateMessage = new SensorData();
                updateMessage.Product = productName;
                updateMessage.Path = string.Empty;
                updateMessage.TransactionType = TransactionType.Delete;
                updateMessage.Time = timeCollected;

                _queueManager.AddSensorData(updateMessage);

                result = true;
            }
            catch (Exception ex)
            {
                result = false;
                error = ex.Message;
                _logger.LogError(ex, $"Failed to remove product, name = {productName}");
            }
            return result;
        }

        public bool RemoveProduct(Product product, out string error)
        {
            bool result = false;
            error = string.Empty;
            try
            {
                RemoveProductFromUsers(product);
                _productManager.RemoveProduct(product.Name);
                _valuesCache.RemoveProduct(product.Name);

                DateTime timeCollected = DateTime.UtcNow;
                SensorData updateMessage = new SensorData();
                updateMessage.Product = product.Name;
                updateMessage.TransactionType = TransactionType.Delete;
                updateMessage.Time = timeCollected;

                _queueManager.AddSensorData(updateMessage);

                result = true;
            }
            catch(Exception ex)
            {
                result = false;
                error = ex.Message;
                _logger.LogError(ex, $"Failed to remove product, name = {product.Name}");
            }
            return result;
        }

        public bool HideProduct(Product product, out string error)
        {
            bool result = false;
            error = string.Empty;
            try
            {
                DateTime timeCollected = DateTime.UtcNow;
                SensorData updateMessage = new SensorData();
                updateMessage.Product = product.Name;
                updateMessage.TransactionType = TransactionType.Delete;
                updateMessage.Time = timeCollected;

                _queueManager.AddSensorData(updateMessage);

                result = true;
            }
            catch (Exception ex)
            {
                result = false;
                error = ex.Message;
                _logger.LogError(ex, $"Failed to hide product, name = {product.Name}");
            }
            return result;
        }

        private void RemoveProductFromUsers(Product product)
        {
            var usersToEdit = new List<User>();
            foreach (var user in _userManager.Users)
            {
                var count = user.ProductsRoles.RemoveAll(role => role.Key == product.Key);
                if (count == 0)
                    continue;

                usersToEdit.Add(user);
            }

            foreach (var userToEdt in usersToEdit)
            {
                _userManager.UpdateUser(userToEdt);
            }
        }

        public void UpdateProduct(User user, Product product)
        {
            _productManager.UpdateProduct(product);
        }

        #endregion
        
        public void UserUpdated(User user)
        {
            SensorData message = new SensorData();
            message.TransactionType = TransactionType.UpdateTree;
            _queueManager.AddSensorDataForUser(user, message);
        }

        public void AddUpdate(SensorData update)
        {
            _queueManager.AddSensorData(update);
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
                HashComputer.ComputePasswordHash(commonName), true);
            result.Item1 = clientCert;
            result.Item2 = CertificatesConfig.CACertificate;
            return result;
        }

        public ClientVersionModel GetLastAvailableClientVersion()
        {
            return _configurationProvider.ClientVersion;
        }

        public void Dispose()
        {
            var lastBarsValues = _barsStorage.GetAllLastValues();
            foreach (var lastBarValue in lastBarsValues)
            {
                ProcessExtendedBarData(lastBarValue);
            }
            _barsStorage?.Dispose();
        }
    }
}