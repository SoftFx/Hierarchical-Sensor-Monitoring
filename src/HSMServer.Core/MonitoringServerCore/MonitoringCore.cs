using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Converters;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
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
using HSMServer.Core.SensorsDataValidation;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;

namespace HSMServer.Core.MonitoringServerCore
{
    public class MonitoringCore : ISensorsInterface, IDisposable
    {
        private static readonly (byte[], string) _defaultFileSensorData = (Array.Empty<byte>(), string.Empty);

        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly IBarSensorsStorage _barsStorage;
        private readonly IProductManager _productManager;
        private readonly ILogger<MonitoringCore> _logger;
        private readonly IUpdatesQueue _updatesQueue;
        private readonly ITreeValuesCache _treeValuesCache;

        public MonitoringCore(IDatabaseAdapter databaseAdapter, IBarSensorsStorage barsStorage,
            IProductManager productManager, IConfigurationProvider configurationProvider,
            IUpdatesQueue updatesQueue, ITreeValuesCache treeValuesCache, ILogger<MonitoringCore> logger)
        {
            _logger = logger;
            _databaseAdapter = databaseAdapter;
            _barsStorage = barsStorage;
            _barsStorage.IncompleteBarOutdated += BarsStorage_IncompleteBarOutdated;

            _productManager = productManager;

            _updatesQueue = updatesQueue;
            _updatesQueue.NewItemsEvent += UpdatesQueueNewItemsHandler;

            _treeValuesCache = treeValuesCache;

            Thread.Sleep(1000);

            _logger.LogInformation("Monitoring core initialized");

            SensorDataValidationExtensions.Initialize(configurationProvider);
        }

        private void UpdatesQueueNewItemsHandler(IEnumerable<SensorValueBase> sensorValues)
        {
            foreach (var value in sensorValues)
                AddSensorValue(value);
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

        public void AddSensor(string productName, SensorValueBase sensorValue)
        {
            var product = _productManager.GetProductByName(productName);
            if (product == null) return;

            var newSensor = sensorValue.Convert(productName);

            product.AddOrUpdateSensor(newSensor);
            _databaseAdapter.AddSensor(newSensor);
        }

        public void RemoveSensorsData(string productId) =>
            _treeValuesCache.RemoveSensorsData(productId);

        public void RemoveSensorData(Guid sensorId) =>
            _treeValuesCache.RemoveSensorData(sensorId);

        public void RemoveSensor(string productName, string path)
        {
            var product = _productManager.GetProductByName(productName);
            if (product == null) return;

            try
            {
                // TODO: remove sensor value from cache
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
                RemoveSensor(product, path);
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

        public void UpdateSensor(UpdatedSensor updatedSensor) =>
            _treeValuesCache.UpdateSensor(updatedSensor);

        public bool IsSensorRegistered(string productName, string path) =>
            _productManager.GetProductByName(productName)?.Sensors.ContainsKey(path) ?? false;

        public void AddSensorValue<T>(T value) where T : SensorValueBase
        {
            try
            {
                DateTime timeCollected = DateTime.UtcNow;

                var validationResult = value.Validate();
                if (!CheckValidationResult(value, validationResult))
                    return;

                var productName = _treeValuesCache.GetProductNameById(value.Key);
                if (!ProcessBarSensorValue(value, productName, timeCollected))
                    return;

                _treeValuesCache.AddNewSensorValue(value, timeCollected, validationResult);
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

        private bool CheckValidationResult(SensorValueBase value, SensorsDataValidation.ValidationResult validationResult)
        {
            if (validationResult.IsError)
            {
                _logger.LogError($"Sensor data validation {validationResult.ResultType}(s). Sensor: '{value?.Path}', error(s): '{validationResult.Error}'");
                return false;
            }
            else if (validationResult.IsWarning)
                _logger.LogWarning($"Sensor data validation {validationResult.ResultType}(s). Sensor: '{value?.Path}', warning(s): '{validationResult.Warning}'");

            return true;
        }

        private bool ProcessBarSensorValue(SensorValueBase value, string product, DateTime timeCollected)
        {
            if (value is BarSensorValueBase barSensorValue)
                return ProcessBarSensorValue(barSensorValue, product, timeCollected);
            else if (value is UnitedSensorValue unitedSensorValue && unitedSensorValue.IsBarSensor())
                return ProcessBarSensorValue(unitedSensorValue.Convert(), product, timeCollected);

            return true;
        }

        private bool ProcessBarSensorValue(BarSensorValueBase value, string product, DateTime timeCollected)
        {
            if (value.EndTime == DateTime.MinValue)
            {
                _barsStorage.Add(value, product, timeCollected);
                return false;
            }

            _barsStorage.Remove(product, value.Path);
            return true;
        }

        #endregion

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
                return (FileSensorContentCompressionHelper.GetDecompressedContent(sensorHistoryData, fileData), fileData.Extension);
            }
            catch
            {
                return _defaultFileSensorData;
            }
        }

        #endregion

        public void Dispose()
        {
            var lastBarsValues = _barsStorage.GetAllLastValues();
            foreach (var lastBarValue in lastBarsValues)
                ProcessExtendedBarData(lastBarValue);

            _updatesQueue?.Dispose();
            _barsStorage?.Dispose();
        }
    }
}