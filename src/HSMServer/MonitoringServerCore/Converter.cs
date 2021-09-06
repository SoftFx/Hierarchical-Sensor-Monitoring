using HSMCommon.Model;
using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.DataLayer.Model;
using HSMServer.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using HSMDatabase.Entity;

namespace HSMServer.MonitoringServerCore
{
    internal class Converter : IConverter
    {
        private readonly ILogger<Converter> _logger;
        private const double SIZE_DENOMINATOR = 1024.0;

        //public SignedCertificateMessage Convert(X509Certificate2 signedCertificate,
        //    X509Certificate2 caCertificate)
        //{
        //    SignedCertificateMessage message = new SignedCertificateMessage();
        //    message.CaCertificateBytes = ByteString.CopyFrom(caCertificate.Export(X509ContentType.Cert));
        //    message.SignedCertificateBytes = ByteString.CopyFrom(signedCertificate.Export(X509ContentType.Pfx));
        //    return message;
        //}

        public Converter(ILogger<Converter> logger)
        {
            _logger = logger;
        }

        #region Deserialize

        public BoolSensorValue GetBoolSensorValue(string json)
        {
            return JsonSerializer.Deserialize<BoolSensorValue>(json);
        }

        public IntSensorValue GetIntSensorValue(string json)
        {
            return JsonSerializer.Deserialize<IntSensorValue>(json);
        }

        public DoubleSensorValue GetDoubleSensorValue(string json)
        {
            return JsonSerializer.Deserialize<DoubleSensorValue>(json);
        }

        public StringSensorValue GetStringSensorValue(string json)
        {
            return JsonSerializer.Deserialize<StringSensorValue>(json);
        }
        public IntBarSensorValue GetIntBarSensorValue(string json)
        {
            return JsonSerializer.Deserialize<IntBarSensorValue>(json);
        }

        public DoubleBarSensorValue GetDoubleBarSensorValue(string json)
        {
            return JsonSerializer.Deserialize<DoubleBarSensorValue>(json);
        }

        public FileSensorValue GetFileSensorValue(string json)
        {
            return JsonSerializer.Deserialize<FileSensorValue>(json);
        }

        #endregion

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
       
        public SensorHistoryData Convert(SensorDataEntity dataObject)
        {
            SensorHistoryData historyData = new SensorHistoryData();
            try
            {
                historyData.TypedData = dataObject.TypedData;
                historyData.Time = dataObject.Time;
                historyData.SensorType = (SensorType)dataObject.DataType;
            }
            catch (Exception e)
            { }

            return historyData;
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
        public SensorDataEntity ConvertToDatabase(BoolSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = (byte)SensorType.BooleanSensor;
            result.Status = (byte)sensorValue.Status;

            BoolSensorData typedData = new BoolSensorData() { BoolValue = sensorValue.BoolValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public SensorDataEntity ConvertToDatabase(IntSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = (byte)SensorType.IntSensor;
            result.Status = (byte)sensorValue.Status;

            IntSensorData typedData = new IntSensorData() { IntValue = sensorValue.IntValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public SensorDataEntity ConvertToDatabase(DoubleSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = (byte)SensorType.DoubleSensor;
            result.Status = (byte)sensorValue.Status;

            DoubleSensorData typedData = new DoubleSensorData() { DoubleValue = sensorValue.DoubleValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public SensorDataEntity ConvertToDatabase(StringSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = (byte)SensorType.StringSensor;
            result.Status = (byte)sensorValue.Status;

            StringSensorData typedData = new StringSensorData() { StringValue = sensorValue.StringValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public SensorDataEntity ConvertToDatabase(FileSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = (byte)SensorType.FileSensor;
            result.Status = (byte)sensorValue.Status;

            FileSensorData typedData = new FileSensorData()
            {
                Comment = sensorValue.Comment,
                Extension = sensorValue.Extension,
                FileContent = sensorValue.FileContent,
                FileName = sensorValue.FileName
            };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;

        }

        public SensorDataEntity ConvertToDatabase(FileSensorBytesValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = (byte)SensorType.FileSensor;
            result.Status = (byte)sensorValue.Status;

            FileSensorBytesData typedData = new FileSensorBytesData()
            {
                Comment = sensorValue.Comment,
                Extension = sensorValue.Extension,
                FileContent = sensorValue.FileContent,
                FileName = sensorValue.FileName
            };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }
        public SensorDataEntity ConvertToDatabase(IntBarSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = (byte)SensorType.IntegerBarSensor;

            IntBarSensorData typedData = ToTypedData(sensorValue);
            result.TypedData = JsonSerializer.Serialize(typedData);
            result.Status = (byte)sensorValue.Status;
            return result;
        }

        public SensorDataEntity ConvertToDatabase(DoubleBarSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = (byte)SensorType.DoubleBarSensor;

            DoubleBarSensorData typedData = ToTypedData(sensorValue);
            result.TypedData = JsonSerializer.Serialize(typedData);
            result.Status = (byte)sensorValue.Status;
            return result;
        }

        //private void FillCommonFields(SensorValueBase value, DateTime timeCollected, out SensorDataObject dataObject)
        //{
        //    dataObject = new SensorDataObject();
        //    dataObject.Path = value.Path;
        //    dataObject.Time = value.Time.ToUniversalTime();
        //    dataObject.TimeCollected = timeCollected.ToUniversalTime();
        //    dataObject.Timestamp = GetTimestamp(value.Time);
        //}

        //public SensorDataObject ConvertToDatabase(BoolSensorValue sensorValue, DateTime timeCollected)
        //{
        //    SensorDataObject result;
        //    FillCommonFields(sensorValue, timeCollected, out result);
        //    result.DataType = SensorType.BooleanSensor;
        //    result.Status = sensorValue.Status;

        //    BoolSensorData typedData = new BoolSensorData() { BoolValue = sensorValue.BoolValue, Comment = sensorValue.Comment };
        //    result.TypedData = JsonSerializer.Serialize(typedData);
        //    return result;
        //}

        //public SensorDataObject ConvertToDatabase(IntSensorValue sensorValue, DateTime timeCollected)
        //{
        //    FillCommonFields(sensorValue, timeCollected, out var result);
        //    result.DataType = SensorType.IntSensor;
        //    result.Status = sensorValue.Status;

        //    IntSensorData typedData = new IntSensorData() { IntValue = sensorValue.IntValue, Comment = sensorValue.Comment };
        //    result.TypedData = JsonSerializer.Serialize(typedData);
        //    return result;
        //}

        //public SensorDataObject ConvertToDatabase(DoubleSensorValue sensorValue, DateTime timeCollected)
        //{
        //    FillCommonFields(sensorValue, timeCollected, out var result);
        //    result.DataType = SensorType.DoubleSensor;
        //    result.Status = sensorValue.Status;

        //    DoubleSensorData typedData = new DoubleSensorData() { DoubleValue = sensorValue.DoubleValue, Comment = sensorValue.Comment };
        //    result.TypedData = JsonSerializer.Serialize(typedData);
        //    return result;
        //}

        //public SensorDataObject ConvertToDatabase(StringSensorValue sensorValue, DateTime timeCollected)
        //{
        //    FillCommonFields(sensorValue, timeCollected, out var result);
        //    result.DataType = SensorType.StringSensor;
        //    result.Status = sensorValue.Status;

        //    StringSensorData typedData = new StringSensorData() { StringValue = sensorValue.StringValue, Comment = sensorValue.Comment };
        //    result.TypedData = JsonSerializer.Serialize(typedData);
        //    return result;
        //}

        //public SensorDataObject ConvertToDatabase(FileSensorValue sensorValue, DateTime timeCollected)
        //{
        //    FillCommonFields(sensorValue, timeCollected, out var result);
        //    result.DataType = SensorType.FileSensor;
        //    result.Status = sensorValue.Status;

        //    FileSensorData typedData = new FileSensorData()
        //    {
        //        Comment = sensorValue.Comment, Extension = sensorValue.Extension, FileContent = sensorValue.FileContent, FileName = sensorValue.FileName
        //    };
        //    result.TypedData = JsonSerializer.Serialize(typedData);
        //    return result;

        //}

        //public SensorDataObject ConvertToDatabase(FileSensorBytesValue sensorValue, DateTime timeCollected)
        //{
        //    FillCommonFields(sensorValue, timeCollected, out var result);
        //    result.DataType = SensorType.FileSensor;
        //    result.Status = sensorValue.Status;

        //    FileSensorBytesData typedData = new FileSensorBytesData()
        //    {
        //        Comment = sensorValue.Comment,
        //        Extension = sensorValue.Extension,
        //        FileContent = sensorValue.FileContent,
        //        FileName = sensorValue.FileName
        //    };
        //    result.TypedData = JsonSerializer.Serialize(typedData);
        //    return result;
        //}
        //public SensorDataObject ConvertToDatabase(IntBarSensorValue sensorValue, DateTime timeCollected)
        //{
        //    FillCommonFields(sensorValue, timeCollected, out var result);
        //    result.DataType = SensorType.IntegerBarSensor;

        //    IntBarSensorData typedData = ToTypedData(sensorValue);
        //    result.TypedData = JsonSerializer.Serialize(typedData);
        //    result.Status = sensorValue.Status;
        //    return result;
        //}

        //public SensorDataObject ConvertToDatabase(DoubleBarSensorValue sensorValue, DateTime timeCollected)
        //{
        //    FillCommonFields(sensorValue, timeCollected, out var result);
        //    result.DataType = SensorType.DoubleBarSensor;

        //    DoubleBarSensorData typedData = ToTypedData(sensorValue);
        //    result.TypedData = JsonSerializer.Serialize(typedData);
        //    result.Status = sensorValue.Status;
        //    return result;
        //}


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

        public SensorData Convert(SensorDataEntity dataObject, SensorInfo sensorInfo, string productName)
        {
            var converted = Convert(dataObject, productName);
            converted.Description = sensorInfo.Description;
            return converted;
        }
        public SensorData Convert(SensorDataEntity dataObject, string productName)
        {
            SensorData result = new SensorData();
            result.Path = dataObject.Path;
            result.SensorType = (SensorType)dataObject.DataType;
            result.Product = productName;
            result.Time = dataObject.TimeCollected;
            result.StringValue = GetStringValue(dataObject.TypedData, (SensorType)dataObject.DataType, dataObject.TimeCollected);
            result.ShortStringValue = GetShortStringValue(dataObject.TypedData, (SensorType)dataObject.DataType);
            result.Status = (SensorStatus)dataObject.Status;
            return result;
        }

        public SensorData Convert(BoolSensorValue value, string productName, DateTime timeCollected, TransactionType type)
        {
            AddCommonValues(value, productName, timeCollected, type, out var data);
            data.StringValue = GetStringValue(value, timeCollected);
            data.ShortStringValue = GetShortStringValue(value);
            data.SensorType = SensorType.BooleanSensor;
            return data;
        }

        public SensorData Convert(IntSensorValue value, string productName, DateTime timeCollected, TransactionType type)
        {
            AddCommonValues(value, productName, timeCollected, type, out var data);
            data.StringValue = GetStringValue(value, timeCollected);
            data.ShortStringValue = GetShortStringValue(value);
            data.SensorType = SensorType.IntSensor;
            return data;
        }

        public SensorData Convert(DoubleSensorValue value, string productName, DateTime timeCollected, TransactionType type)
        {
            AddCommonValues(value, productName, timeCollected, type, out var data);
            data.StringValue = GetStringValue(value, timeCollected);
            data.ShortStringValue = GetShortStringValue(value);
            data.SensorType = SensorType.DoubleSensor;
            return data;
        }

        public SensorData Convert(StringSensorValue value, string productName, DateTime timeCollected, TransactionType type)
        {
            AddCommonValues(value, productName, timeCollected, type, out var data);
            data.StringValue = GetStringValue(value, timeCollected);
            data.ShortStringValue = GetShortStringValue(value);
            data.SensorType = SensorType.StringSensor;
            return data;
        }

        public SensorData Convert(FileSensorValue value, string productName, DateTime timeCollected, TransactionType type)
        {
            AddCommonValues(value, productName, timeCollected, type, out var data);
            data.StringValue = GetStringValue(value, timeCollected);
            data.ShortStringValue = GetShortStringValue(value);
            data.SensorType = SensorType.FileSensor;
            return data;
        }

        public SensorData Convert(FileSensorBytesValue value, string productName, DateTime timeCollected,
            TransactionType type)
        {
            AddCommonValues(value, productName, timeCollected, type, out var data);
            data.StringValue = GetStringValue(value, timeCollected);
            data.ShortStringValue = GetShortStringValue(value);
            data.SensorType = SensorType.FileSensorBytes;
            return data;
        }
        public SensorData Convert(IntBarSensorValue value, string productName, DateTime timeCollected, TransactionType type)
        {
            AddCommonValues(value, productName, timeCollected, type, out var data);
            data.StringValue = GetStringValue(value, timeCollected);
            data.ShortStringValue = GetShortStringValue(value);
            data.SensorType = SensorType.IntegerBarSensor;
            return data;
        }
        public SensorData Convert(DoubleBarSensorValue value, string productName, DateTime timeCollected, TransactionType type)
        {
            AddCommonValues(value, productName, timeCollected, type, out var data);
            data.StringValue = GetStringValue(value, timeCollected);
            data.ShortStringValue = GetShortStringValue(value);
            data.SensorType = SensorType.DoubleBarSensor;
            return data;
        }

        private void AddCommonValues(SensorValueBase value, string productName, DateTime timeCollected, TransactionType type, out SensorData data)
        {
            data = new SensorData();
            data.Path = value.Path;
            data.Product = productName;
            data.Time = timeCollected;
            data.TransactionType = type;
            data.Description = value.Description;
            data.Status = value.Status;
            data.Key = value.Key;
        }

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
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {boolValue}, comment = {comment}"
                : $"Time: {timeCollected.ToUniversalTime():G}. Value = {boolValue}.";
        }

        private string GetStringValueForInt(int intValue, DateTime timeCollected, string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {intValue}, comment = {comment}"
                : $"Time: {timeCollected.ToUniversalTime():G}. Value = {intValue}.";
        }

        private string GetStringValueForDouble(double doubleValue, DateTime timeCollected, string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {doubleValue}, comment = {comment}"
                : $"Time: {timeCollected.ToUniversalTime():G}. Value = {doubleValue}.";
        }

        private string GetStringValueForString(string stringValue, DateTime timeCollected, string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value = '{stringValue}', comment = {comment}"
                : $"Time: {timeCollected.ToUniversalTime():G}. Value = '{stringValue}'.";
        }

        private string GetStringValueForIntBar(int min, int max, int mean, int count, int last, DateTime timeCollected,
            string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {min}, Mean = {mean}, Max = {max}, Count = {count}, Last = {last}. Comment = {comment}"
                : $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {min}, Mean = {mean}, Max = {max}, Count = {count}, Last = {last}.";
        }

        private string GetStringValueForDoubleBar(double min, double max, double mean, int count, double last,
            DateTime timeCollected, string comment)
        {
            return !string.IsNullOrEmpty(comment)
                ? $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {min}, Mean = {mean}, Max = {max}, Count = {count}, Last = {last}. Comment = {comment}"
                : $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {min}, Mean = {mean}, Max = {max}, Count = {count}, Last = {last}.";
        }
        private string GetStringValue(string stringData, SensorType sensorType, DateTime timeCollected)
        {
            string result = string.Empty;
            switch (sensorType)
            {
                case SensorType.BooleanSensor:
                    {
                        try
                        {
                            BoolSensorData boolData = JsonSerializer.Deserialize<BoolSensorData>(stringData);
                            return GetStringValueForBool(boolData.BoolValue, timeCollected, boolData.Comment);
                        }
                        catch { }
                        break;
                    }
                case SensorType.IntSensor:
                    {
                        try
                        {
                            IntSensorData intData = JsonSerializer.Deserialize<IntSensorData>(stringData);
                            return GetStringValueForInt(intData.IntValue, timeCollected, intData.Comment);
                        }
                        catch { }
                        break;
                    }
                case SensorType.DoubleSensor:
                    {
                        try
                        {
                            DoubleSensorData doubleData = JsonSerializer.Deserialize<DoubleSensorData>(stringData);
                            return GetStringValueForDouble(doubleData.DoubleValue, timeCollected, doubleData.Comment);
                        }
                        catch
                        { }
                        break;
                    }
                case SensorType.StringSensor:
                    {
                        try
                        {
                            StringSensorData stringTypedData = JsonSerializer.Deserialize<StringSensorData>(stringData);
                            return GetStringValueForString(stringTypedData.StringValue, timeCollected,
                                stringTypedData.Comment);
                        }
                        catch { }
                        break;
                    }
                case SensorType.IntegerBarSensor:
                    {
                        try
                        {
                            IntBarSensorData intBarData = JsonSerializer.Deserialize<IntBarSensorData>(stringData);
                            return GetStringValueForIntBar(intBarData.Min, intBarData.Max, intBarData.Mean, intBarData.Count,
                                intBarData.LastValue, timeCollected, intBarData.Comment);
                        }
                        catch { }
                        break;
                    }
                case SensorType.DoubleBarSensor:
                    {
                        try
                        {
                            DoubleBarSensorData doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(stringData);
                            return GetStringValueForDoubleBar(doubleBarData.Min, doubleBarData.Max, doubleBarData.Mean,
                                doubleBarData.Count,
                                doubleBarData.LastValue, timeCollected, doubleBarData.Comment);
                        }
                        catch { }
                        break;
                    }
                case SensorType.FileSensor:
                    {
                        try
                        {
                            FileSensorData fileData = JsonSerializer.Deserialize<FileSensorData>(stringData);
                            string sizeString = FileSizeToNormalString(fileData?.FileContent?.Length ?? 0);
                            string fileNameString = GetFileNameString(fileData.FileName, fileData.Extension);
                            result = !string.IsNullOrEmpty(fileData.Comment)
                                ? $"Time: {timeCollected.ToUniversalTime():G}. File size: {sizeString}. {fileNameString} Comment = {fileData.Comment}."
                                : $"Time: {timeCollected.ToUniversalTime():G}. File size: {sizeString}. {fileNameString}";
                        }
                        catch { }
                        break;
                    }
                case SensorType.FileSensorBytes:
                    {
                    try
                    {
                        FileSensorData fileData = JsonSerializer.Deserialize<FileSensorData>(stringData);
                        string sizeString = FileSizeToNormalString(fileData?.FileContent?.Length ?? 0);
                        string fileNameString = GetFileNameString(fileData.FileName, fileData.Extension);
                        result = !string.IsNullOrEmpty(fileData.Comment)
                            ? $"Time: {timeCollected.ToUniversalTime():G}. File size: {sizeString}. {fileNameString} Comment = {fileData.Comment}."
                            : $"Time: {timeCollected.ToUniversalTime():G}. File size: {sizeString}. {fileNameString}";
                    }
                    catch { }
                    break;
                    }
                default:
                {
                    result = string.Empty;
                    break;
                }
            }
            return result;
        }

        private string GetShortStringValue(string stringData, SensorType sensorType)
        {
            string result = string.Empty;
            switch (sensorType)
            {
                case SensorType.BooleanSensor:
                    {
                        try
                        {
                            BoolSensorData boolData = JsonSerializer.Deserialize<BoolSensorData>(stringData);
                            result = boolData.BoolValue.ToString();
                        }
                        catch { }
                        break;
                    }
                case SensorType.IntSensor:
                    {
                        try
                        {
                            IntSensorData intData = JsonSerializer.Deserialize<IntSensorData>(stringData);
                            result = intData.IntValue.ToString();
                        }
                        catch { }
                        break;
                    }
                case SensorType.DoubleSensor:
                    {
                        try
                        {
                            DoubleSensorData doubleData = JsonSerializer.Deserialize<DoubleSensorData>(stringData);
                            result = doubleData.DoubleValue.ToString();
                        }
                        catch { }
                        break;
                    }
                case SensorType.StringSensor:
                    {
                        try
                        {
                            StringSensorData stringTypedData = JsonSerializer.Deserialize<StringSensorData>(stringData);
                            result = stringTypedData.StringValue;
                        }
                        catch { }
                        break;
                    }
                case SensorType.IntegerBarSensor:
                    {
                        try
                        {
                            IntBarSensorData intBarData = JsonSerializer.Deserialize<IntBarSensorData>(stringData);
                            result =
                                $"Min = {intBarData.Min}, Mean = {intBarData.Mean}, Max = {intBarData.Max}, Count = {intBarData.Count}, Last = {intBarData.LastValue}.";
                        }
                        catch { }
                        break;
                    }
                case SensorType.DoubleBarSensor:
                    {
                        try
                        {
                            DoubleBarSensorData doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(stringData);
                            result =
                                $"Min = {doubleBarData.Min}, Mean = {doubleBarData.Mean}, Max = {doubleBarData.Max}, Count = {doubleBarData.Count}, Last = {doubleBarData.LastValue}.";
                        }
                        catch { }
                        break;
                    }
                case SensorType.FileSensor:
                    {
                        try
                        {
                            FileSensorData fileData = JsonSerializer.Deserialize<FileSensorData>(stringData);
                            string sizeString = FileSizeToNormalString(fileData?.FileContent?.Length ?? 0);
                            string fileNameString = GetFileNameString(fileData.FileName, fileData.Extension);
                            result = $"File size: {sizeString}. {fileNameString}";
                        }
                        catch { }
                        break;
                    }
                case SensorType.FileSensorBytes:
                    {
                        try
                        {
                            FileSensorData fileData = JsonSerializer.Deserialize<FileSensorData>(stringData);
                            string sizeString = FileSizeToNormalString(fileData?.FileContent?.Length ?? 0);
                            string fileNameString = GetFileNameString(fileData.FileName, fileData.Extension);
                            result = $"File size: {sizeString}. {fileNameString}";
                        }
                        catch { }
                        break;
                    }
                default:
                    {
                        result = string.Empty;
                        break;
                    }
            }
            return result;
        }
        private string GetStringValue(BoolSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = !string.IsNullOrEmpty(value.Comment)
                    ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.BoolValue}, comment = {value.Comment}."
                    : $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.BoolValue}.";
            }
            catch (Exception e)
            {
                _logger.LogError(e,"Failed to get short value");
            }

            return result;
        }

        private string GetStringValue(IntSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = !string.IsNullOrEmpty(value.Comment)
                    ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.IntValue}, comment = {value.Comment}."
                    : $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.IntValue}.";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }

            return result;
        }

        private string GetStringValue(DoubleSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = !string.IsNullOrEmpty(value.Comment)
                    ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.DoubleValue}, comment = {value.Comment}."
                    : $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.DoubleValue}.";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }

            return result;
        }

        private string GetStringValue(StringSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = !string.IsNullOrEmpty(value.Comment)
                    ? $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.StringValue}, comment = {value.Comment}."
                    : $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.StringValue}.";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }

            return result;
        }

        private string GetStringValue(FileSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                string sizeString = FileSizeToNormalString(value?.FileContent?.Length ?? 0);
                string fileNameString = GetFileNameString(value.FileName, value.Extension);
                result = !string.IsNullOrEmpty(value.Comment)
                    ? $"Time: {timeCollected.ToUniversalTime():G}. File size: {sizeString}. {fileNameString} Comment = {value.Comment}."
                    : $"Time: {timeCollected.ToUniversalTime():G}. File size: {sizeString}. {fileNameString}";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }

            return result;
        }
        private string GetStringValue(FileSensorBytesValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                string sizeString = FileSizeToNormalString(value?.FileContent?.Length ?? 0);
                string fileNameString = GetFileNameString(value.FileName, value.Extension);
                result = !string.IsNullOrEmpty(value.Comment)
                    ? $"Time: {timeCollected.ToUniversalTime():G}. File size: {sizeString}. {fileNameString} Comment = {value.Comment}."
                    : $"Time: {timeCollected.ToUniversalTime():G}. File size: {sizeString}. {fileNameString}";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }

            return result;
        }
        private string GetStringValue(IntBarSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = !string.IsNullOrEmpty(value.Comment)
                    ? $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {value.Min}, Mean = {value.Mean}, Max = {value.Max}, Count = {value.Count}, Last = {value.LastValue}. Comment = {value.Comment}."
                    : $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {value.Min}, Mean = {value.Mean}, Max = {value.Max}, Count = {value.Count}, Last = {value.LastValue}.";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }

            return result;
        }

        private string GetStringValue(DoubleBarSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = !string.IsNullOrEmpty(value.Comment)
                    ? $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {value.Min}, Mean = {value.Mean}, Max = {value.Max}, Count = {value.Count}, Last = {value.LastValue}. Comment = {value.Comment}."
                    : $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {value.Min}, Mean = {value.Mean}, Max = {value.Max}, Count = {value.Count}, Last = {value.LastValue}.";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }

            return result;
        }

        private string GetShortStringValue(BoolSensorValue value)
        {
            return value.BoolValue.ToString();
        }

        private string GetShortStringValue(IntSensorValue value)
        {
            return value.IntValue.ToString();
        }

        private string GetShortStringValue(DoubleSensorValue value)
        {
            return value.DoubleValue.ToString();
        }

        private string GetShortStringValue(StringSensorValue value)
        {
            return value.StringValue;
        }

        private string GetShortStringValue(IntBarSensorValue value)
        {
            string result = string.Empty;
            try
            {
                result =
                    $"Min = {value.Min}, Mean = {value.Mean}, Max = {value.Max}, Count = {value.Count}, Last = {value.LastValue}.";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }
            return result;
        }

        private string GetShortStringValue(DoubleBarSensorValue value)
        {
            string result = string.Empty;
            try
            {
                result =
                    $"Min = {value.Min}, Mean = {value.Mean}, Max = {value.Max}, Count = {value.Count}, Last = {value.LastValue}.";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }
            return result;
        }

        private string GetShortStringValue(FileSensorValue value)
        {
            string result = string.Empty;
            try
            {
                string sizeString = FileSizeToNormalString(value?.FileContent?.Length ?? 0);
                string fileNameString = GetFileNameString(value.FileName, value.Extension);
                result = $"File size: {sizeString}. {fileNameString}";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }

            return result;
        }

        private string GetShortStringValue(FileSensorBytesValue value)
        {
            string result = string.Empty;
            try
            {
                string sizeString = FileSizeToNormalString(value?.FileContent?.Length ?? 0);
                string fileNameString = GetFileNameString(value.FileName, value.Extension);
                result = $"File size: {sizeString}. {fileNameString}";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get short value");
            }

            return result;
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
        public SensorDataEntity ConvertUnitedValueToDatabase(UnitedSensorValue value, DateTime timeCollected)
        {
            FillCommonFields(value, timeCollected, out var result);
            result.DataType = (byte)value.Type;
            result.Status = (byte)value.Status;
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
        public SensorInfo Convert(string productName, string path)
        {
            SensorInfo result = new SensorInfo();
            result.Path = path;
            result.ProductName = productName;
            result.SensorName = ExtractSensor(path);
            return result;
        }
        public SensorInfo Convert(string productName, SensorValueBase sensorValue)
        {
            SensorInfo result = new SensorInfo();
            result.Path = sensorValue.Path;
            result.Description = sensorValue.Description;
            result.ProductName = productName;
            result.SensorName = ExtractSensor(sensorValue.Path);
            return result;
        }
        //public ProductDataMessage Convert(Product product)
        //{
        //    ProductDataMessage result = new ProductDataMessage();
        //    result.Name = product.Name;
        //    result.Key = product.Key;
        //    result.DateAdded = product.DateAdded.ToUniversalTime().ToTimestamp();
        //    return result;
        //}

        //public GenerateClientCertificateModel Convert(CertificateRequestMessage requestMessage)
        //{
        //    GenerateClientCertificateModel model = new GenerateClientCertificateModel
        //    {
        //        CommonName = requestMessage.CommonName,
        //        CountryName = requestMessage.CountryName,
        //        EmailAddress = requestMessage.EmailAddress,
        //        LocalityName = requestMessage.LocalityName,
        //        OrganizationName = requestMessage.OrganizationName,
        //        OrganizationUnitName = requestMessage.OrganizationUnitName,
        //        StateOrProvinceName = requestMessage.StateOrProvinceName
        //    };
        //    return model;
        //}

        //public RSAParameters Convert(HSMService.RSAParameters rsaParameters)
        //{
        //    RSAParameters result = new RSAParameters();
        //    result.D = rsaParameters.D.ToByteArray();
        //    result.DP = rsaParameters.DP.ToByteArray();
        //    result.DQ = rsaParameters.DQ.ToByteArray();
        //    result.Exponent = rsaParameters.Exponent.ToByteArray();
        //    result.InverseQ = rsaParameters.InverseQ.ToByteArray();
        //    result.Modulus = rsaParameters.Modulus.ToByteArray();
        //    result.P = rsaParameters.P.ToByteArray();
        //    result.Q = rsaParameters.Q.ToByteArray();
        //    return result;
        //}

        #region Sub-methods

        //private SensorObjectType Convert(SensorType type)
        //{
        //    //return (SensorObjectType) ((int) type);
        //    switch (type)
        //    {
        //        case SensorType.BooleanSensor:
        //            return SensorObjectType.ObjectTypeBoolSensor;
        //        case SensorType.DoubleSensor:
        //            return SensorObjectType.ObjectTypeDoubleSensor;
        //        case SensorType.IntSensor:
        //            return SensorObjectType.ObjectTypeIntSensor;
        //        case SensorType.StringSensor:
        //            return SensorObjectType.ObjectTypeStringSensor;
        //        case SensorType.IntegerBarSensor:
        //            return SensorObjectType.ObjectTypeBarIntSensor;
        //        case SensorType.DoubleBarSensor:
        //            return SensorObjectType.ObjectTypeBarDoubleSensor;
        //        case SensorType.FileSensor:
        //            return SensorObjectType.ObjectTypeFileSensor;
        //    }
        //    throw new Exception($"Unknown SensorDataType = {type}!");
        //}
        private long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = (dateTime - DateTime.UnixEpoch);
            return (long)timeSpan.TotalSeconds;
        }

        public void ExtractProductAndSensor(string path, out string server, out string sensor)
        {
            server = string.Empty;
            sensor = string.Empty;
            var splitRes = path.Split("/".ToCharArray());
            server = splitRes[0];
            sensor = splitRes[^1];
        }

        public string ExtractSensor(string path)
        {
            var splitRes = path.Split("/".ToCharArray());
            return splitRes[^1];
        }

        private string GetFileNameString(string fileName, string extension)
        {
            if (string.IsNullOrEmpty(extension) && string.IsNullOrEmpty(fileName))
            {
                return "No file info specified!";
            }
            if (string.IsNullOrEmpty(fileName))
            {
                return $"Extension: {extension}.";
            }

            if (fileName.IndexOf('.') != -1)
            {
                return $"File name: {fileName}.";
            }

            return $"File name: {fileName}.{extension}.";
        }
        private string FileSizeToNormalString(int size)
        {
            if (size < SIZE_DENOMINATOR)
            {
                return $"{size} bytes";
            }

            double kb = size / SIZE_DENOMINATOR;
            if (kb < SIZE_DENOMINATOR)
            {
                return $"{kb:#,##0} KB";
            }

            double mb = kb / SIZE_DENOMINATOR;
            if (mb < SIZE_DENOMINATOR)
            {
                return $"{mb:#,##0.0} MB";
            }

            double gb = mb / SIZE_DENOMINATOR;
            return $"{gb:#,##0.0} GB";
        }
        #endregion

    }
}
