using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
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

        #region Convert to history items

        public SensorHistoryData Convert(ExtendedBarSensorData data)
        {
            switch (data.ValueType)
            {
                case SensorType.DoubleBarSensor:
                    return Convert(data.Value as DoubleBarSensorValue, data.TimeCollected);
                case SensorType.IntegerBarSensor:
                    return Convert(data.Value as IntBarSensorValue, data.TimeCollected);
                default:
                    return null;
            }
        }

        private SensorHistoryData Convert(IntBarSensorValue value, DateTime timeCollected)
        {
            SensorHistoryData result = new SensorHistoryData();
            try
            {
                result.TypedData = JsonSerializer.Serialize(ToTypedData(value));
                result.Time = value.Time.ToUniversalTime();
                result.SensorType = SensorType.IntegerBarSensor;
            }
            catch (Exception e)
            { }

            return result;
        }
        private SensorHistoryData Convert(DoubleBarSensorValue value, DateTime timeCollected)
        {
            SensorHistoryData result = new SensorHistoryData();
            try
            {
                result.TypedData = JsonSerializer.Serialize(ToTypedData(value));
                result.Time = value.Time.ToUniversalTime();
                result.SensorType = SensorType.DoubleBarSensor;
            }
            catch (Exception e)
            { }

            return result;
        }

        #endregion
        #region Convert to database objects

        private void FillCommonFields(SensorValueBase value, DateTime timeCollected, out SensorDataEntity dataObject)
        {
            dataObject = new SensorDataEntity();
            dataObject.Path = value.Path;
            dataObject.Time = value.Time.ToUniversalTime();
            dataObject.TimeCollected = timeCollected.ToUniversalTime();
            dataObject.Timestamp = GetTimestamp(value.Time);
        }

        private IntBarSensorData ToTypedData(IntBarSensorValue sensorValue)
        {
            IntBarSensorData typedData = new IntBarSensorData()
            {
                Max = sensorValue.Max,
                Min = sensorValue.Min,
                Mean = sensorValue.Mean,
                Count = sensorValue.Count,
                Comment = sensorValue.Comment,
                StartTime = sensorValue.StartTime.ToUniversalTime(),
                Percentiles = sensorValue.Percentiles,
                LastValue = sensorValue.LastValue
            };
            typedData.EndTime = (sensorValue.EndTime == DateTime.MinValue)
                ? DateTime.Now.ToUniversalTime() 
                : sensorValue.EndTime.ToUniversalTime();
            return typedData;
        }

        private DoubleBarSensorData ToTypedData(DoubleBarSensorValue sensorValue)
        {
            DoubleBarSensorData typedData = new DoubleBarSensorData()
            {
                Max = sensorValue.Max,
                Min = sensorValue.Min,
                Mean = sensorValue.Mean,
                Count = sensorValue.Count,
                Comment = sensorValue.Comment,
                StartTime = sensorValue.StartTime.ToUniversalTime(),
                Percentiles = sensorValue.Percentiles,
                LastValue = sensorValue.LastValue
            };
            typedData.EndTime = (sensorValue.EndTime == DateTime.MinValue) 
                ? DateTime.Now.ToUniversalTime() 
                : sensorValue.EndTime.ToUniversalTime();
            return typedData;
        }
        #endregion
        
        #region Independent update messages
        private void AddCommonValues(UnitedSensorValue value, string productName, DateTime timeCollected, out SensorData data)
        {
            data = new SensorData();
            data.Path = value.Path;
            data.Product = productName;
            data.Time = timeCollected;
            data.Description = value.Description;
            data.Status = value.Status;
            data.Key = value.Key;
            data.SensorType = value.Type;
        }
        #endregion
        #region Typed data objects

        private string GetStringValueForBool(bool boolValue, DateTime timeCollected, string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {boolValue}, comment = {comment}."
                : $"Time: {timeCollected.ToUniversalTime():G}. Value = {boolValue}.";
        }

        private string GetStringValueForInt(int intValue, DateTime timeCollected, string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {intValue}, comment = {comment}."
                : $"Time: {timeCollected.ToUniversalTime():G}. Value = {intValue}.";
        }

        private string GetStringValueForDouble(double doubleValue, DateTime timeCollected, string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {doubleValue}, comment = {comment}."
                : $"Time: {timeCollected.ToUniversalTime():G}. Value = {doubleValue}.";
        }

        private string GetStringValueForString(string stringValue, DateTime timeCollected, string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {stringValue}, comment = {comment}."
                : $"Time: {timeCollected.ToUniversalTime():G}. Value = {stringValue}.";
        }

        private string GetStringValueForIntBar(int min, int max, int mean, int count, int last, DateTime timeCollected,
            string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {min}, Mean = {mean}, Max = {max}, Count = {count}, Last = {last}. Comment = {comment}."
                : $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {min}, Mean = {mean}, Max = {max}, Count = {count}, Last = {last}.";
        }

        private string GetStringValueForDoubleBar(double min, double max, double mean, int count, double last,
            DateTime timeCollected, string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {min}, Mean = {mean}, Max = {max}, Count = {count}, Last = {last}. Comment = {comment}."
                : $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {min}, Mean = {mean}, Max = {max}, Count = {count}, Last = {last}.";
        }

        #endregion

        #region UnitedSensorValue to update messages
        public SensorData ConvertUnitedValue(UnitedSensorValue value, string productName,
            DateTime timeCollected, TransactionType type)
        {
            AddCommonValues(value, productName, timeCollected, out var data);
            data.StringValue = GetStringValue(value, timeCollected);
            data.ShortStringValue = GetShortStringValue(value);
            data.TransactionType = type;
            return data;
        }

        private string GetStringValue(UnitedSensorValue value, DateTime timeCollected)
        {
            switch (value.Type)
            {
                case SensorType.BooleanSensor:
                    bool boolRes = bool.Parse(value.Data);
                    return GetStringValueForBool(boolRes, timeCollected, value.Comment);
                case SensorType.IntSensor:
                    int intRes = int.Parse(value.Data);
                    return GetStringValueForInt(intRes, timeCollected, value.Comment);
                case SensorType.DoubleSensor:
                    double doubleRes = double.Parse(value.Data);
                    return GetStringValueForDouble(doubleRes, timeCollected, value.Comment);
                case SensorType.StringSensor:
                    return GetStringValueForString(value.Data, timeCollected, value.Comment);
                case SensorType.IntegerBarSensor:
                    IntBarData intBarData = JsonSerializer.Deserialize<IntBarData>(value.Data);
                    return GetStringValueForIntBar(intBarData.Min, intBarData.Max, intBarData.Mean, intBarData.Count, intBarData.LastValue,
                        timeCollected, value.Comment);
                case SensorType.DoubleBarSensor:
                    DoubleBarData doubleBarData = JsonSerializer.Deserialize<DoubleBarData>(value.Data);
                    return GetStringValueForDoubleBar(doubleBarData.Min, doubleBarData.Max, doubleBarData.Mean, doubleBarData.Count, doubleBarData.LastValue,
                        timeCollected, value.Comment);
            }

            return string.Empty;
        }
        private string GetShortStringValue(UnitedSensorValue value)
        {
            switch (value.Type)
            {
                //Simple data types store TypedValue.ToString()
                case SensorType.BooleanSensor:
                case SensorType.IntSensor:
                case SensorType.DoubleSensor:
                case SensorType.StringSensor:
                    return value.Data;
                case SensorType.IntegerBarSensor:
                {
                    try
                    {
                        var typedDataObj = JsonSerializer.Deserialize<IntBarData>(value.Data);
                        return 
                            $"Min = {typedDataObj.Min}, Mean = {typedDataObj.Mean}, Max = {typedDataObj.Max}, Count = {typedDataObj.Count}, Last = {typedDataObj.LastValue}.";
                    }
                    catch (Exception e)
                    {
                        return string.Empty;
                    }
                }
                case SensorType.DoubleBarSensor:
                {
                    try
                    {
                        var typedDataObj = JsonSerializer.Deserialize<DoubleBarData>(value.Data);
                        return
                            $"Min = {typedDataObj.Min}, Mean = {typedDataObj.Mean}, Max = {typedDataObj.Max}, Count = {typedDataObj.Count}, Last = {typedDataObj.LastValue}.";
                    }
                    catch (Exception e)
                    {
                        return string.Empty;
                    }
                }
            }

            return string.Empty;
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
                intBarObj.StartTime = data.StartTime;
                intBarObj.EndTime = data.EndTime;
            }
            catch (Exception e)
            { }
        }

        public SensorInfo Convert(string productName, SensorValueBase sensorValue)
        {
            SensorInfo result = new SensorInfo();
            result.Path = sensorValue.Path;
            result.Description = sensorValue.Description;
            result.ProductName = productName;
            result.SensorName = ExtractSensor(sensorValue.Path);
            result.SensorType = GetSensorType(sensorValue);
            return result;
        }

        private static SensorType GetSensorType(SensorValueBase sensorValue) =>
            sensorValue switch
            {
                BoolSensorValue => SensorType.BooleanSensor,
                IntSensorValue => SensorType.IntSensor,
                DoubleSensorValue => SensorType.DoubleSensor,
                StringSensorValue => SensorType.StringSensor,
                IntBarSensorValue => SensorType.IntegerBarSensor,
                DoubleBarSensorValue => SensorType.DoubleBarSensor,
                FileSensorBytesValue => SensorType.FileSensorBytes,
                FileSensorValue => SensorType.FileSensor,
                _ => (SensorType)0,
            };

        #region Sub-methods

        private long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = (dateTime - DateTime.UnixEpoch);
            return (long)timeSpan.TotalSeconds;
        }

        private string ExtractSensor(string path)
        {
            var splitRes = path.Split("/".ToCharArray());
            return splitRes[^1];
        }
        #endregion

    }
}
