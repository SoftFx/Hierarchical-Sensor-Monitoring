using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model.Sensor;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;

namespace HSMServer.Core.MonitoringServerCore
{
    public class Converter : IConverter
    {
        private readonly ILogger<Converter> _logger;

        public Converter(ILogger<Converter> logger)
        {
            _logger = logger;
        }

        #region Convert to database objects

        private void FillCommonFields(SensorValueBase value, DateTime timeCollected, out SensorDataEntity dataObject)
        {
            dataObject = new SensorDataEntity();
            dataObject.Path = value.Path;
            dataObject.Time = value.Time.ToUniversalTime();
            dataObject.TimeCollected = timeCollected.ToUniversalTime();
            dataObject.Timestamp = GetTimestamp(value.Time);
        }

        #endregion

        #region UnitedSensorValue to database objects
        public SensorDataEntity ConvertUnitedValueToDatabase(UnitedSensorValue value, DateTime timeCollected, SensorStatus validationStatus)
        {
            FillCommonFields(value, timeCollected, out var result);
            result.DataType = (byte)value.Type;
            result.Status = (byte)value.Status.GetWorst(validationStatus);
            result.TypedData = GetTypedData(value);
            return result;
        }

        private string GetTypedData(UnitedSensorValue value)
        {
            switch (value.Type)
            {
                case SensorType.BooleanSensor:
                    return GetBoolTypedData(value.Data, value.Comment);
                case SensorType.IntSensor:
                    return GetIntTypedData(value.Data, value.Comment);
                case SensorType.DoubleSensor:
                    return GetDoubleTypedData(value.Data, value.Comment);
                case SensorType.StringSensor:
                    return GetStringTypedData(value.Data, value.Comment);
                case SensorType.DoubleBarSensor:
                    return GetDoubleBarTypedData(value.Data, value.Comment);
                case SensorType.IntegerBarSensor:
                    return GetIntBarTypedData(value.Data, value.Comment);
                default:
                    return "";
            }
        }

        private string GetBoolTypedData(string val, string comment)
        {
            BoolSensorData data = new BoolSensorData();
            var isParsed = bool.TryParse(val, out bool result);
            if (isParsed)
            {
                data.BoolValue = result;
            }
            else
            {
                _logger.LogError($"Failed to parse boolean from '{val}'");
            }

            data.Comment = comment;
            return JsonSerializer.Serialize(data);
        }

        private string GetIntTypedData(string val, string comment)
        {
            IntSensorData data = new IntSensorData();
            var isParsed = int.TryParse(val, out var result);
            if (isParsed)
            {
                data.IntValue = result;
            }
            else
            {
                _logger.LogError($"Failed to parse integer from '{val}'");
            }

            data.Comment = comment;
            return JsonSerializer.Serialize(data);
        }

        private string GetDoubleTypedData(string val, string comment)
        {
            DoubleSensorData data = new DoubleSensorData();
            var isParsed = double.TryParse(val, out var result);
            if (isParsed)
            {
                data.DoubleValue = result;
            }
            else
            {
                _logger.LogError($"Failed to parse double from '{val}'");
            }

            data.Comment = comment;
            return JsonSerializer.Serialize(data);
        }

        private string GetStringTypedData(string val, string comment)
        {
            StringSensorData data = new StringSensorData();
            data.StringValue = val;
            data.Comment = comment;
            return JsonSerializer.Serialize(data);
        }

        private string GetIntBarTypedData(string val, string comment)
        {
            IntBarData data;
            try
            {
                data = JsonSerializer.Deserialize<IntBarData>(val);
            }
            catch (Exception e)
            {
                _logger.LogError(e,$"Failed to deserialize intBarData from {val}");
                return "";
            }

            IntBarSensorData result = new IntBarSensorData
            {
                Count = data.Count,
                Max = data.Max,
                Min = data.Min,
                Mean = data.Mean,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
                LastValue = data.LastValue,
                Comment = comment
            };
            //result.Percentiles.AddRange(data.Percentiles);
            result.Percentiles = data.Percentiles;
            return JsonSerializer.Serialize(result);
        }

        private string GetDoubleBarTypedData(string val, string comment)
        {
            DoubleBarData data;
            try
            {
                data = JsonSerializer.Deserialize<DoubleBarData>(val);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to deserialize intBarData from {val}");
                return "";
            }

            DoubleBarSensorData result = new DoubleBarSensorData
            {
                Count = data.Count,
                Max = data.Max,
                Min = data.Min,
                Mean = data.Mean,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
                LastValue = data.LastValue,
                Comment = comment
            };
            //result.Percentiles.AddRange(data.Percentiles);
            result.Percentiles = data.Percentiles;
            return JsonSerializer.Serialize(result);
        }
        #endregion

        public BarSensorValueBase GetBarSensorValue(UnitedSensorValue value)
        {
            BarSensorValueBase result;
            switch (value.Type)
            {
                case SensorType.DoubleBarSensor:
                    result = new DoubleBarSensorValue();
                    CopyCommonFields(value, result);
                    CopyDoubleBarData(value, (DoubleBarSensorValue) result);
                    return result;
                case SensorType.IntegerBarSensor:
                    result = new IntBarSensorValue();
                    CopyCommonFields(value, result);
                    CopyIntBarData(value, (IntBarSensorValue) result);
                    return result;
            }

            return null;
        }

        private void CopyCommonFields(UnitedSensorValue unitedObj, BarSensorValueBase barObj)
        {
            barObj.Comment = unitedObj.Comment;
            barObj.Path = unitedObj.Path;
            barObj.Description = unitedObj.Description;
            barObj.Status = unitedObj.Status;
            barObj.Key = unitedObj.Key;
            barObj.Time = unitedObj.Time;
        }

        private void CopyIntBarData(UnitedSensorValue unitedObj, IntBarSensorValue intBarObj)
        {
            try
            {
                IntBarData data = JsonSerializer.Deserialize<IntBarData>(unitedObj.Data);
                intBarObj.Max = data.Max;
                intBarObj.Mean = data.Mean;
                intBarObj.Min = data.Min;
                intBarObj.Percentiles = data.Percentiles;
                intBarObj.LastValue = data.LastValue;
                intBarObj.Count = data.Count;
                intBarObj.StartTime = data.StartTime;
                intBarObj.EndTime = data.EndTime;
            }
            catch (Exception e)
            { }
        }
        private void CopyDoubleBarData(UnitedSensorValue unitedObj, DoubleBarSensorValue intBarObj)
        {
            try
            {
                DoubleBarData data = JsonSerializer.Deserialize<DoubleBarData>(unitedObj.Data);
                intBarObj.Max = data.Max;
                intBarObj.Mean = data.Mean;
                intBarObj.Min = data.Min;
                intBarObj.Percentiles = data.Percentiles;
                intBarObj.LastValue = data.LastValue;
                intBarObj.Count = data.Count;
                intBarObj.StartTime = data.StartTime;
                intBarObj.EndTime = data.EndTime;
            }
            catch (Exception e)
            { }
        }

        #region Sub-methods

        private long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = (dateTime - DateTime.UnixEpoch);
            return (long)timeSpan.TotalSeconds;
        }
        #endregion

    }
}
