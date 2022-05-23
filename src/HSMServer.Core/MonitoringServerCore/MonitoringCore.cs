using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Configuration;
using HSMServer.Core.Converters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringCoreInterface;
using HSMServer.Core.SensorsDataValidation;
using HSMServer.Core.SensorsUpdatesQueue;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace HSMServer.Core.MonitoringServerCore
{
    public class MonitoringCore : ISensorsInterface, IDisposable
    {
        private static readonly (byte[], string) _defaultFileSensorData = (Array.Empty<byte>(), string.Empty);

        private readonly IDatabaseCore _databaseCore;
        private readonly IBarSensorsStorage _barsStorage;
        private readonly ILogger<MonitoringCore> _logger;
        private readonly IUpdatesQueue _updatesQueue;
        private readonly ITreeValuesCache _treeValuesCache;


        public MonitoringCore(IDatabaseCore databaseCore, IBarSensorsStorage barsStorage, IConfigurationProvider configurationProvider,
            IUpdatesQueue updatesQueue, ITreeValuesCache treeValuesCache, ILogger<MonitoringCore> logger)
        {
            _logger = logger;
            _databaseCore = databaseCore;
            _barsStorage = barsStorage;
            _barsStorage.IncompleteBarOutdated += BarsStorage_IncompleteBarOutdated;

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
            _databaseCore.PutSensorData(dataObject, productName);
        }

        public void RemoveSensorsData(string productId) =>
            _treeValuesCache.RemoveSensorsData(productId);

        public void RemoveSensorData(Guid sensorId) =>
            _treeValuesCache.RemoveSensorData(sensorId);

        public void UpdateSensor(SensorUpdate updatedSensor) =>
            _treeValuesCache.UpdateSensor(updatedSensor);

        public void AddSensorValue<T>(T value) where T : SensorValueBase
        {
            try
            {
                DateTime timeCollected = DateTime.UtcNow;

                var validationResult = value.Validate();
                if (!CheckValidationResult(value, validationResult))
                    return;

                var productName = _treeValuesCache.GetProductNameById(value.Key);

                if (productName != null && !ProcessBarSensorValue(value, productName, timeCollected))
                    return;

                _treeValuesCache.AddNewSensorValue(value, timeCollected, validationResult);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add value for sensor '{value?.Path}'");
            }
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

        #region Sensors History

        public List<SensorHistoryData> GetSensorHistory(string product, string path, DateTime from, DateTime to)
        {
            var historyValues = _databaseCore.GetSensorHistory(product, path,
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
            var allValues = _databaseCore.GetAllSensorHistory(product, path);
            var lastValue = _barsStorage.GetLastValue(product, path);
            if (lastValue != null)
            {
                allValues.Add(lastValue.Convert());
            }
            return allValues;
        }

        public List<SensorHistoryData> GetSensorHistory(string product, string path, int n)
        {
            List<SensorHistoryData> historyList = _databaseCore.GetSensorHistory(product, path, n);
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
            var sensorHistoryData = _databaseCore.GetOneValueSensorValue(product, path);

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