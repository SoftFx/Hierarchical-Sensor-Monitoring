﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Authentication;
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
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.SensorsDataValidation;

namespace HSMServer.Core.MonitoringServerCore
{
    public class MonitoringCore : IMonitoringDataReceiver, ISensorsInterface, IMonitoringUpdatesReceiver, IDisposable
    {
        private static readonly (byte[], string) _defaultFileSensorData = (Array.Empty<byte>(), string.Empty);

        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly IBarSensorsStorage _barsStorage;
        private readonly IMonitoringQueueManager _queueManager;
        private readonly IUserManager _userManager;
        private readonly IProductManager _productManager;
        private readonly ILogger<MonitoringCore> _logger;
        private readonly IValuesCache _valuesCache;

        public MonitoringCore(IDatabaseAdapter databaseAdapter, IUserManager userManager, IBarSensorsStorage barsStorage,
            IProductManager productManager, IConfigurationProvider configurationProvider,
            IValuesCache valuesVCache, ILogger<MonitoringCore> logger)
        {
            _logger = logger;
            _databaseAdapter = databaseAdapter;
            _barsStorage = barsStorage;
            _barsStorage.IncompleteBarOutdated += BarsStorage_IncompleteBarOutdated;

            _userManager = userManager;
            _userManager.UpdateUserEvent += UpdateUserEventHandler;

            _queueManager = new MonitoringQueueManager();

            _productManager = productManager;
            _productManager.RemovedProduct += RemoveProductHandler;

            _valuesCache = valuesVCache;

            Thread.Sleep(5000);
            FillValuesCache();

            _logger.LogInformation("Monitoring core initialized");

            SensorDataValidationExtensions.Initialize(configurationProvider);
        }

        private void FillValuesCache()
        {
            var productsList = _productManager.Products;
            foreach (var product in productsList)
            {
                var sensors = GetProductSensors(product.Name);
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

        public void AddSensor(string productName, SensorValueBase sensorValue)
        {
            var product = _productManager.GetProductByName(productName);
            if (product == null) return;

            var newSensor = sensorValue.Convert(productName);

            product.AddOrUpdateSensor(newSensor);
            _databaseAdapter.AddSensor(newSensor);
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
                    AddSensorValue(value);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Failed to add data for {value?.Path}");
                }

            }
        }

        #endregion

        public void RemoveSensor(string productName, string path)
        {
            var product = _productManager.GetProductByName(productName);
            if (product == null) return;

            try
            {
                product.RemoveSensor(path);
                _databaseAdapter.RemoveSensor(productName, path);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error while removing sensor {path} for {productName}");
            }
        }

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
                RemoveSensor(product, path);
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

        public void UpdateSensorInfo(SensorInfo newInfo)
        {
            var existingInfo = GetSensorInfo(newInfo.ProductName, newInfo.Path);
            if (existingInfo == null) return;

            existingInfo.Update(newInfo);

            _productManager.GetProductByName(newInfo.ProductName)?.AddOrUpdateSensor(existingInfo);

            _databaseAdapter.UpdateSensor(existingInfo);
        }

        public bool IsSensorRegistered(string productName, string path) =>
            _productManager.GetProductByName(productName)?.Sensors.ContainsKey(path) ?? false;

        public void AddSensorValue<T>(T value) where T : SensorValueBase
        {
            try
            {
                DateTime timeCollected = DateTime.UtcNow;

                var validationResult = value.Validate();

                if (validationResult.IsError)
                {
                    _logger.LogError($"Sensor data validation {validationResult.ResultType}(s). Sensor: '{value?.Path}', error(s): '{validationResult.Error}'");
                    return;
                }
                else if (validationResult.IsWarning)
                    _logger.LogWarning($"Sensor data validation {validationResult.ResultType}(s). Sensor: '{value?.Path}', warning(s): '{validationResult.Warning}'");

                var sensorData = GetSensorData(value, timeCollected, validationResult);

                _queueManager.AddSensorData(sensorData);
                _valuesCache.AddValue(sensorData.Product, sensorData);

                if (!ProcessBarSensorValue(value, sensorData))
                    return;

                SensorDataEntity databaseObj = value.Convert(timeCollected, sensorData.Status);
                SaveSensorValue(databaseObj, sensorData.Product);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
        }

        public SensorInfo GetSensorInfo(string productName, string path)
        {
            SensorInfo value = null;

            return _productManager.GetProductByName(productName)?.Sensors.TryGetValue(path, out value)
                ?? false ? value : null;
        }

        private bool ProcessBarSensorValue(SensorValueBase value, SensorData sensorData)
        {
            if (value is BarSensorValueBase barSensorValue)
                return ProcessBarSensorValue(barSensorValue, sensorData);
            else if (value is UnitedSensorValue unitedSensorValue && unitedSensorValue.IsBarSensor())
                return ProcessBarSensorValue(unitedSensorValue.Convert(), sensorData);

            return true;
        }

        private bool ProcessBarSensorValue(BarSensorValueBase value, SensorData sensorData)
        {
            if (value.EndTime == DateTime.MinValue)
            {
                _barsStorage.Add(value, sensorData);
                return false;
            }

            _barsStorage.Remove(sensorData.Product, value.Path);
            return true;
        }

        private TransactionType AddSensorIfNotRegisteredAndGetTransactionType(string productName, SensorValueBase value)
        {
            var transactionType = TransactionType.Update;

            if (!IsSensorRegistered(productName, value.Path))
            {
                AddSensor(productName, value);
                transactionType = TransactionType.Add;
            }

            return transactionType;
        }

        private SensorData GetSensorData(SensorValueBase value, DateTime timeCollected, SensorsDataValidation.ValidationResult validationResult)
        {
            var productName = _productManager.GetProductNameByKey(value.Key);
            var transactionType = AddSensorIfNotRegisteredAndGetTransactionType(productName, value);
            var sensorStatus = GetSensorStatus(validationResult);

            var sensorData = value.Convert(productName, timeCollected, transactionType);
            sensorData.Status = sensorStatus > sensorData.Status ? sensorStatus : sensorData.Status;
            sensorData.ValidationError = validationResult.Error;

            return sensorData;
        }

        public static SensorStatus GetSensorStatus(SensorsDataValidation.ValidationResult validationResult) =>
            validationResult.ResultType switch
            {
                ResultType.Unknown => SensorStatus.Unknown,
                ResultType.Ok => SensorStatus.Ok,
                ResultType.Warning => SensorStatus.Warning,
                ResultType.Error => SensorStatus.Error,
                _ => throw new InvalidCastException($"Unknown validation result: {validationResult.ResultType}"),
            };

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
            if (!UserRoleHelper.IsAllProductsTreeAllowed(user))
                productsList = productsList.Where(p =>
                ProductRoleHelper.IsAvailable(p.Key, user.ProductsRoles)).ToList();

            result.AddRange(_valuesCache.GetValues(productsList.Select(p => p.Name).ToList()));

            return result;
        }

        public List<SensorInfo> GetProductSensors(string productName) =>
            _productManager.GetProductByName(productName)?.Sensors.Values.ToList();

        #region Sensors History

        public List<SensorHistoryData> GetSensorHistory(string product, string path, DateTime from, DateTime to)
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

        public List<SensorHistoryData> GetAllSensorHistory(string product, string path)
        {
            var allValues = _databaseAdapter.GetAllSensorHistory(product, path);
            var lastValue = _barsStorage.GetLastValue(product, path);
            if (lastValue != null)
            {
                allValues.Add(lastValue.Convert());
            }
            return allValues;
        }

        public List<SensorHistoryData> GetSensorHistory(string product, string path, int n)
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

        public (byte[] content, string extension) GetFileSensorValueData(string product, string path)
        {
            var sensorHistoryData = _databaseAdapter.GetOneValueSensorValue(product, path);

            if (sensorHistoryData == null)
                return _defaultFileSensorData;

            if (sensorHistoryData.SensorType == SensorType.FileSensor)
                sensorHistoryData = sensorHistoryData.ConvertToFileSensorBytes();

            if (sensorHistoryData.SensorType != SensorType.FileSensorBytes)
                return _defaultFileSensorData;

            try
            {
                var fileData = JsonSerializer.Deserialize<FileSensorBytesData>(sensorHistoryData.TypedData);
                return (fileData.FileContent, fileData.Extension);
            }
            catch
            {
                return _defaultFileSensorData;
            }
        }

        #endregion


        #region Product

        private void RemoveProductHandler(Product product)
        {
            var updateMessage = new SensorData
            {
                Product = product.Name,
                Path = string.Empty,
                TransactionType = TransactionType.Delete,
                Time = DateTime.UtcNow
            };

            _userManager.RemoveProductFromUsers(product.Key);
            _queueManager.AddSensorData(updateMessage);
            _valuesCache.RemoveProduct(product.Name);
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

        #endregion

        private void UpdateUserEventHandler(User user) =>
            _queueManager.AddSensorDataForUser(user, new() { TransactionType = TransactionType.UpdateTree });

        public void AddUpdate(SensorData update)
        {
            _queueManager.AddSensorData(update);
        }

        public void Dispose()
        {
            var lastBarsValues = _barsStorage.GetAllLastValues();
            foreach (var lastBarValue in lastBarsValues)
                ProcessExtendedBarData(lastBarValue);

            _barsStorage?.Dispose();
            _queueManager?.Dispose();

            if (_productManager != null)
                _productManager.RemovedProduct -= RemoveProductHandler;

            if (_userManager != null)
                _userManager.UpdateUserEvent -= UpdateUserEventHandler;         
        }
    }
}