﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Configuration;
using HSMServer.Core.Converters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.SensorsDataValidation;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Threading;
using SensorType = HSMSensorDataObjects.SensorType;

namespace HSMServer.Core.MonitoringServerCore
{
    public class MonitoringCore
    {
        private readonly IDatabaseCore _databaseCore;
        private readonly ILogger<MonitoringCore> _logger;


        public MonitoringCore(IDatabaseCore databaseCore,
            IConfigurationProvider configurationProvider, ILogger<MonitoringCore> logger)
        {
            _logger = logger;
            _databaseCore = databaseCore;

            Thread.Sleep(1000);

            _logger.LogInformation("Monitoring core initialized");

            SensorDataValidationExtensions.Initialize(configurationProvider);
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

        private bool ProcessBarSensorValue(BaseValue value, string product, DateTime timeCollected)
        {
            if (value is BarBaseValue barSensorValue)
                return ProcessBarSensorValue(barSensorValue, product, timeCollected);
            //else if (value is UnitedSensorValue unitedSensorValue && unitedSensorValue.IsBarSensor())
            //    return ProcessBarSensorValue(unitedSensorValue.Convert(), product, timeCollected);

            return true;
        }

        #endregion
    }
}