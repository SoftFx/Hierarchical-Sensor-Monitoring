using System;
using System.Text.Json;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.DataLayer.Model;
using HSMServer.Model;
using HSMServer.Model.SensorsData;
using NLog;

namespace HSMServer.MonitoringServerCore
{
    public static class Converter
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        //public static SignedCertificateMessage Convert(X509Certificate2 signedCertificate,
        //    X509Certificate2 caCertificate)
        //{
        //    SignedCertificateMessage message = new SignedCertificateMessage();
        //    message.CaCertificateBytes = ByteString.CopyFrom(caCertificate.Export(X509ContentType.Cert));
        //    message.SignedCertificateBytes = ByteString.CopyFrom(signedCertificate.Export(X509ContentType.Pfx));
        //    return message;
        //}

        

        #region Deserialize

        public static BoolSensorValue GetBoolSensorValue(string json)
        {
            return JsonSerializer.Deserialize<BoolSensorValue>(json);
        }

        public static IntSensorValue GetIntSensorValue(string json)
        {
            return JsonSerializer.Deserialize<IntSensorValue>(json);
        }

        public static DoubleSensorValue GetDoubleSensorValue(string json)
        {
            return JsonSerializer.Deserialize<DoubleSensorValue>(json);
        }

        public static StringSensorValue GetStringSensorValue(string json)
        {
            return JsonSerializer.Deserialize<StringSensorValue>(json);
        }
        public static IntBarSensorValue GetIntBarSensorValue(string json)
        {
            return JsonSerializer.Deserialize<IntBarSensorValue>(json);
        }

        public static DoubleBarSensorValue GetDoubleBarSensorValue(string json)
        {
            return JsonSerializer.Deserialize<DoubleBarSensorValue>(json);
        }

        public static FileSensorValue GetFileSensorValue(string json)
        {
            return JsonSerializer.Deserialize<FileSensorValue>(json);
        }

        #endregion

        #region Convert to history items

        public static SensorHistoryData Convert(ExtendedBarSensorData data)
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

        //private static SensorHistoryMessage Convert(IntBarSensorValue value, DateTime timeCollected)
        //{
        //    SensorHistoryMessage result = new SensorHistoryMessage();
        //    try
        //    {
        //        result.TypedData = JsonSerializer.Serialize(ToTypedData(value));
        //        result.Time = Timestamp.FromDateTime(value.Time.ToUniversalTime());
        //        result.Type = SensorObjectType.ObjectTypeBarIntSensor;
        //    }
        //    catch (Exception e)
        //    {

        //    }
        //    return result;
        //}
        private static SensorHistoryData Convert(IntBarSensorValue value, DateTime timeCollected)
        {
            SensorHistoryData result = new SensorHistoryData();
            try
            {
                result.TypedData = JsonSerializer.Serialize(ToTypedData(value));
                result.Time = value.Time;
                result.SensorType = SensorType.IntegerBarSensor;
            }
            catch (Exception e)
            { }

            return result;
        }
        //private static SensorHistoryMessage Convert(DoubleBarSensorValue value, DateTime timeCollected)
        //{
        //    SensorHistoryMessage result = new SensorHistoryMessage();
        //    try
        //    {
        //        result.TypedData = JsonSerializer.Serialize(ToTypedData(value));
        //        result.Time = Timestamp.FromDateTime(value.Time.ToUniversalTime());
        //        result.Type = SensorObjectType.ObjectTypeBarDoubleSensor;
        //    }
        //    catch (Exception e)
        //    {

        //    }
        //    return result;
        //}
        private static SensorHistoryData Convert(DoubleBarSensorValue value, DateTime timeCollected)
        {
            SensorHistoryData result = new SensorHistoryData();
            try
            {
                result.TypedData = JsonSerializer.Serialize(ToTypedData(value));
                result.Time = value.Time;
                result.SensorType = SensorType.DoubleBarSensor;
            }
            catch (Exception e)
            { }

            return result;
        }
        //public static SensorHistoryMessage Convert(SensorDataObject dataObject)
        //{
        //    SensorHistoryMessage result = new SensorHistoryMessage();
        //    try
        //    {
        //        result.TypedData = dataObject.TypedData;
        //        result.Time = Timestamp.FromDateTime(dataObject.Time.ToUniversalTime());
        //        result.Type = Convert(dataObject.DataType);
        //    }
        //    catch (Exception e)
        //    {
                
        //    }
        //    return result;
        //}
        public static SensorHistoryData Convert(SensorDataObject dataObject)
        {
            SensorHistoryData historyData = new SensorHistoryData();
            try
            {
                historyData.TypedData = dataObject.TypedData;
                historyData.Time = dataObject.Time;
                historyData.SensorType = dataObject.DataType;
            }
            catch (Exception e)
            { }

            return historyData;
        }

        #endregion
        #region Convert to database objects

        private static void FillCommonFields(SensorValueBase value, DateTime timeCollected, out SensorDataObject dataObject)
        {
            dataObject = new SensorDataObject();
            dataObject.Path = value.Path;
            dataObject.Time = value.Time.ToUniversalTime();
            dataObject.TimeCollected = timeCollected.ToUniversalTime();
            dataObject.Timestamp = GetTimestamp(value.Time);
        }

        //private static SensorType Convert(SensorObjectType type)
        //{
        //    switch (type)
        //    {
        //        case SensorObjectType.ObjectTypeBoolSensor:
        //            return SensorType.BooleanSensor;
        //        case SensorObjectType.ObjectTypeDoubleSensor:
        //            return SensorType.DoubleSensor;
        //        case SensorObjectType.ObjectTypeIntSensor:
        //            return SensorType.IntSensor;
        //        case SensorObjectType.ObjectTypeStringSensor:
        //            return SensorType.StringSensor;
        //        case SensorObjectType.ObjectTypeBarDoubleSensor:
        //            return SensorType.DoubleBarSensor;
        //        case SensorObjectType.ObjectTypeBarIntSensor:
        //            return SensorType.IntegerBarSensor;
        //        case SensorObjectType.ObjectTypeFileSensor:
        //            return SensorType.FileSensor;
        //        default:
        //            throw new InvalidEnumArgumentException($"Invalid SensorDataType: {type}");
        //    }
        //}
        //public static SensorDataObject ConvertToDatabase(SensorUpdateMessage update, DateTime originalTime)
        //{
        //    SensorDataObject result = new SensorDataObject();
        //    result.Path = update.Path;
        //    result.Time = originalTime;
        //    result.TypedData = update.DataObject.ToString(Encoding.UTF8);
        //    result.TimeCollected = update.Time.ToDateTime();
        //    result.Timestamp = GetTimestamp(result.TimeCollected);
        //    result.DataType = Convert(update.ObjectType);
        //    return result;
        //}

        public static SensorDataObject ConvertToDatabase(BoolSensorValue sensorValue, DateTime timeCollected)
        {
            SensorDataObject result;
            FillCommonFields(sensorValue, timeCollected, out result);
            result.DataType = SensorType.BooleanSensor;
            result.Status = sensorValue.Status;

            BoolSensorData typedData = new BoolSensorData() { BoolValue = sensorValue.BoolValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(IntSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = SensorType.IntSensor;
            result.Status = sensorValue.Status;

            IntSensorData typedData = new IntSensorData() { IntValue = sensorValue.IntValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(DoubleSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = SensorType.DoubleSensor;
            result.Status = sensorValue.Status;

            DoubleSensorData typedData = new DoubleSensorData() { DoubleValue = sensorValue.DoubleValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(StringSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = SensorType.StringSensor;
            result.Status = sensorValue.Status;

            StringSensorData typedData = new StringSensorData() { StringValue = sensorValue.StringValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(FileSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = SensorType.FileSensor;
            result.Status = sensorValue.Status;

            FileSensorData typedData = new FileSensorData()
            {
                Comment = sensorValue.Comment, Extension = sensorValue.Extension, FileContent = sensorValue.FileContent
            };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;

        }
        public static SensorDataObject ConvertToDatabase(IntBarSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = SensorType.IntegerBarSensor;

            IntBarSensorData typedData = ToTypedData(sensorValue);
            result.TypedData = JsonSerializer.Serialize(typedData);
            result.Status = sensorValue.Status;
            return result;
        }

        public static SensorDataObject ConvertToDatabase(DoubleBarSensorValue sensorValue, DateTime timeCollected)
        {
            SensorDataObject result;
            FillCommonFields(sensorValue, timeCollected, out result);
            result.DataType = SensorType.DoubleBarSensor;

            DoubleBarSensorData typedData = ToTypedData(sensorValue);
            result.TypedData = JsonSerializer.Serialize(typedData);
            result.Status = sensorValue.Status;
            return result;
        }


        private static IntBarSensorData ToTypedData(IntBarSensorValue sensorValue)
        {
            IntBarSensorData typedData = new IntBarSensorData()
            {
                Max = sensorValue.Max,
                Min = sensorValue.Min,
                Mean = sensorValue.Mean,
                Count = sensorValue.Count,
                Comment = sensorValue.Comment,
                StartTime = sensorValue.StartTime.ToUniversalTime(),
                EndTime = sensorValue.EndTime.ToUniversalTime(),
                Percentiles = sensorValue.Percentiles,
                LastValue = sensorValue.LastValue
            };
            return typedData;
        }

        private static DoubleBarSensorData ToTypedData(DoubleBarSensorValue sensorValue)
        {
            DoubleBarSensorData typedData = new DoubleBarSensorData()
            {
                Max = sensorValue.Max,
                Min = sensorValue.Min,
                Mean = sensorValue.Mean,
                Count = sensorValue.Count,
                Comment = sensorValue.Comment,
                StartTime = sensorValue.StartTime.ToUniversalTime(),
                EndTime = sensorValue.EndTime.ToUniversalTime(),
                Percentiles = sensorValue.Percentiles,
                LastValue = sensorValue.LastValue
            };
            return typedData;
        }
        #endregion


        #region Convert to update messages

        //public static SensorUpdateMessage Convert(SensorDataObject dataObject, string productName)
        //{
        //    SensorUpdateMessage result = new SensorUpdateMessage();
        //    result.Path = dataObject.Path;
        //    result.ObjectType = Convert(dataObject.DataType);
        //    result.Product = productName;
        //    result.Time = Timestamp.FromDateTime(dataObject.TimeCollected.ToUniversalTime());
        //    result.ShortValue = GetShortValue(dataObject.TypedData, dataObject.DataType, dataObject.TimeCollected);
        //    result.Status = Convert(dataObject.Status);
        //    return result;
        //}
        
        //public static SensorUpdateMessage Convert(BoolSensorValue value, string productName, DateTime timeCollected)
        //{
        //    SensorUpdateMessage update;
        //    AddCommonValues(value, productName, timeCollected, out update);
        //    update.ShortValue = GetShortValue(value, timeCollected);
        //    update.ObjectType = SensorObjectType.ObjectTypeBoolSensor;
        //    update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;
        //    update.Status = Convert(value.Status);

        //    return update;
        //}

        //public static SensorUpdateMessage Convert(IntSensorValue value, string productName, DateTime timeCollected)
        //{
        //    SensorUpdateMessage update;
        //    AddCommonValues(value, productName, timeCollected, out update);
        //    update.ShortValue = GetShortValue(value, timeCollected);
        //    update.ObjectType = SensorObjectType.ObjectTypeIntSensor;
        //    update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;
        //    update.Status = Convert(value.Status);

        //    return update;
        //}

        //public static SensorUpdateMessage Convert(DoubleSensorValue value, string productName, DateTime timeCollected)
        //{
        //    SensorUpdateMessage update;
        //    AddCommonValues(value, productName, timeCollected, out update);
        //    update.ShortValue = GetShortValue(value, timeCollected);
        //    update.ObjectType = SensorObjectType.ObjectTypeDoubleSensor;
        //    update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;
        //    update.Status = Convert(value.Status);

        //    return update;
        //}

        //public static SensorUpdateMessage Convert(StringSensorValue value, string productName, DateTime timeCollected)
        //{
        //    SensorUpdateMessage update;
        //    AddCommonValues(value, productName, timeCollected, out update);
        //    update.ShortValue = GetShortValue(value, timeCollected);
        //    update.ObjectType = SensorObjectType.ObjectTypeStringSensor;
        //    update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;
        //    update.Status = Convert(value.Status);

        //    return update;
        //}

        //public static SensorUpdateMessage Convert(FileSensorValue value, string productName, DateTime timeCollected)
        //{
        //    AddCommonValues(value, productName, timeCollected, out var update);
        //    update.ShortValue = GetShortValue(value, timeCollected);
        //    update.ObjectType = SensorObjectType.ObjectTypeFileSensor;
        //    update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;
        //    update.Status = Convert(value.Status);

        //    return update;
        //}
        //public static SensorUpdateMessage Convert(IntBarSensorValue value, string productName, DateTime timeCollected)
        //{
        //    SensorUpdateMessage update;
        //    AddCommonValues(value, productName, timeCollected, out update);
        //    update.ShortValue = GetShortValue(value, timeCollected);
        //    update.ObjectType = SensorObjectType.ObjectTypeBarIntSensor;
        //    update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;
        //    update.Status = Convert(value.Status);

        //    return update;
        //}

        //public static SensorUpdateMessage Convert(DoubleBarSensorValue value, string productName, DateTime timeCollected)
        //{
        //    SensorUpdateMessage update;
        //    AddCommonValues(value, productName, timeCollected, out update);
        //    update.ShortValue = GetShortValue(value, timeCollected);
        //    update.ObjectType = SensorObjectType.ObjectTypeBarDoubleSensor;
        //    update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;
        //    update.Status = Convert(value.Status);

        //    return update;
        //}
        //private static void AddCommonValues(SensorValueBase value, string productName, DateTime timeCollected, out SensorUpdateMessage update)
        //{
        //    update = new SensorUpdateMessage();
        //    update.Path = value.Path;
        //    update.Product = productName;
        //    update.Time = Timestamp.FromDateTime(timeCollected.ToUniversalTime());
        //}
        #endregion

        #region Independent update messages

        public static SensorData Convert(SensorDataObject dataObject, string productName)
        {
            SensorData result = new SensorData();
            result.Path = dataObject.Path;
            result.SensorType = dataObject.DataType;
            result.Product = productName;
            result.Time = dataObject.TimeCollected;
            result.ShortValue = GetShortValue(dataObject.TypedData, dataObject.DataType, dataObject.TimeCollected);
            result.Status = dataObject.Status;
            return result;
        }

        public static SensorData Convert(BoolSensorValue value, string productName, DateTime timeCollected)
        {
            AddCommonValues(value, productName, timeCollected, out var data);
            data.ShortValue = GetShortValue(value, timeCollected);
            data.SensorType = SensorType.BooleanSensor;
            data.Status = value.Status;
            return data;
        }

        public static SensorData Convert(IntSensorValue value, string productName, DateTime timeCollected)
        {
            AddCommonValues(value, productName, timeCollected, out var data);
            data.ShortValue = GetShortValue(value, timeCollected);
            data.SensorType = SensorType.BooleanSensor;
            data.Status = value.Status;
            return data;
        }

        public static SensorData Convert(DoubleSensorValue value, string productName, DateTime timeCollected)
        {
            AddCommonValues(value, productName, timeCollected, out var data);
            data.ShortValue = GetShortValue(value, timeCollected);
            data.SensorType = SensorType.BooleanSensor;
            data.Status = value.Status;
            return data;
        }

        public static SensorData Convert(StringSensorValue value, string productName, DateTime timeCollected)
        {
            AddCommonValues(value, productName, timeCollected, out var data);
            data.ShortValue = GetShortValue(value, timeCollected);
            data.SensorType = SensorType.BooleanSensor;
            data.Status = value.Status;
            return data;
        }

        public static SensorData Convert(FileSensorValue value, string productName, DateTime timeCollected)
        {
            AddCommonValues(value, productName, timeCollected, out var data);
            data.ShortValue = GetShortValue(value, timeCollected);
            data.SensorType = SensorType.BooleanSensor;
            data.Status = value.Status;
            return data;
        }
        public static SensorData Convert(IntBarSensorValue value, string productName, DateTime timeCollected)
        {
            AddCommonValues(value, productName, timeCollected, out var data);
            data.ShortValue = GetShortValue(value, timeCollected);
            data.SensorType = SensorType.BooleanSensor;
            data.Status = value.Status;
            return data;
        }
        public static SensorData Convert(DoubleBarSensorValue value, string productName, DateTime timeCollected)
        {
            AddCommonValues(value, productName, timeCollected, out var data);
            data.ShortValue = GetShortValue(value, timeCollected);
            data.SensorType = SensorType.BooleanSensor;
            data.Status = value.Status;
            return data;
        }
        private static void AddCommonValues(SensorValueBase value, string productName, DateTime timeCollected, out SensorData data)
        {
            data = new SensorData();
            data.Path = value.Path;
            data.Product = productName;
            data.Time = timeCollected;
        }

        #endregion
        #region Typed data objects

        private static string GetShortValue(string stringData, SensorType sensorType, DateTime timeCollected)
        {
            string result = string.Empty;
            switch (sensorType)
            {
                case SensorType.BooleanSensor:
                    {
                        try
                        {
                            BoolSensorData boolData = JsonSerializer.Deserialize<BoolSensorData>(stringData);
                            result = $"Time: {timeCollected.ToUniversalTime():G}. Value = {boolData.BoolValue}";
                        }
                        catch { }
                        break;
                    }
                case SensorType.IntSensor:
                    {
                        try
                        {
                            IntSensorData intData = JsonSerializer.Deserialize<IntSensorData>(stringData);
                            result = $"Time: {timeCollected.ToUniversalTime():G}. Value = {intData.IntValue}";
                        }
                        catch { }
                        break;
                    }
                case SensorType.DoubleSensor:
                    {
                        try
                        {
                            DoubleSensorData doubleData = JsonSerializer.Deserialize<DoubleSensorData>(stringData);
                            result = $"Time: {timeCollected.ToUniversalTime():G}. Value = {doubleData.DoubleValue}";
                        }
                        catch { }
                        break;
                    }
                case SensorType.StringSensor:
                    {
                        try
                        {
                            StringSensorData stringTypedData = JsonSerializer.Deserialize<StringSensorData>(stringData);
                            result = $"Time: {timeCollected.ToUniversalTime():G}. Value = '{stringTypedData.StringValue}'";
                        }
                        catch { }
                        break;
                    }
                case SensorType.IntegerBarSensor:
                    {
                        try
                        {
                            IntBarSensorData intBarData = JsonSerializer.Deserialize<IntBarSensorData>(stringData);
                            result = $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {intBarData.Min}, Mean = {intBarData.Mean}, Max = {intBarData.Max}, Count = {intBarData.Count}, Last = {intBarData.LastValue}";
                        }
                        catch { }
                        break;
                    }
                case SensorType.DoubleBarSensor:
                    {
                        try
                        {
                            DoubleBarSensorData doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(stringData);
                            result = $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {doubleBarData.Min}, Mean = {doubleBarData.Mean}, Max = {doubleBarData.Max}, Count = {doubleBarData.Count}, Last = {doubleBarData.LastValue}";
                        }
                        catch { }
                        break;
                    }
                case SensorType.FileSensor:
                    {
                        try
                        {
                            FileSensorData fileData = JsonSerializer.Deserialize<FileSensorData>(stringData);
                            result = $"Time: {timeCollected.ToUniversalTime():G}. File with length of {fileData?.FileContent?.Length} received.";
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
        private static string GetShortValue(BoolSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result =  $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.BoolValue}";
            }
            catch (Exception e)
            {
                _logger.Error(e,"Failed to get short value");
            }

            return result;
        }

        private static string GetShortValue(IntSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.IntValue}";
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get short value");
            }

            return result;
        }

        private static string GetShortValue(DoubleSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.DoubleValue}";
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get short value");
            }

            return result;
        }

        private static string GetShortValue(StringSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = $"Time: {timeCollected.ToUniversalTime():G}. Value = {value.StringValue}";
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get short value");
            }

            return result;
        }

        private static string GetShortValue(FileSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = $"Time: {timeCollected.ToUniversalTime():G}. File with length of {value.FileContent.Length} received.";
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get short value");
            }

            return result;
        }
        private static string GetShortValue(IntBarSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {value.Min}, Mean = {value.Mean}, Max = {value.Max}, Count = {value.Count}, Last = {value.LastValue}";
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get short value");
            }

            return result;
        }

        private static string GetShortValue(DoubleBarSensorValue value, DateTime timeCollected)
        {
            string result = string.Empty;
            try
            {
                result = $"Time: {timeCollected.ToUniversalTime():G}. Value: Min = {value.Min}, Mean = {value.Mean}, Max = {value.Max}, Count = {value.Count}, Last = {value.LastValue}";
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to get short value");
            }

            return result;
        }
        #endregion

        //public static ProductDataMessage Convert(Product product)
        //{
        //    ProductDataMessage result = new ProductDataMessage();
        //    result.Name = product.Name;
        //    result.Key = product.Key;
        //    result.DateAdded = product.DateAdded.ToUniversalTime().ToTimestamp();
        //    return result;
        //}

        //public static GenerateClientCertificateModel Convert(CertificateRequestMessage requestMessage)
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

        //public static RSAParameters Convert(HSMService.RSAParameters rsaParameters)
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

        //private static SensorObjectType Convert(SensorType type)
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
        private static long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = (dateTime - DateTime.UnixEpoch);
            return (long)timeSpan.TotalSeconds;
        }

        public static void ExtractProductAndSensor(string path, out string server, out string sensor)
        {
            server = string.Empty;
            sensor = string.Empty;
            var splitRes = path.Split("/".ToCharArray());
            server = splitRes[0];
            sensor = splitRes[^1];
        }

        public static string ExtractSensor(string path)
        {
            var splitRes = path.Split("/".ToCharArray());
            return splitRes[^1];
        }
        #endregion

    }
}
