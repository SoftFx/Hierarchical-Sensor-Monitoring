using System;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using HSMCommon.Model;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.DataLayer.Model;
using HSMServer.Model;
using HSMService;
using NLog;
using RSAParameters = System.Security.Cryptography.RSAParameters;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace HSMServer.MonitoringServerCore
{
    public static class Converter
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public static SignedCertificateMessage Convert(X509Certificate2 signedCertificate,
            X509Certificate2 caCertificate)
        {
            SignedCertificateMessage message = new SignedCertificateMessage();
            message.CaCertificateBytes = ByteString.CopyFrom(caCertificate.Export(X509ContentType.Cert));
            message.SignedCertificateBytes = ByteString.CopyFrom(signedCertificate.Export(X509ContentType.Pfx));
            return message;
        }

        public static ClientVersionMessage Convert(ClientVersionModel versionModel)
        {
            ClientVersionMessage result = new ClientVersionMessage();
            result.MainVersion = versionModel.MainVersion;
            result.SubVersion = versionModel.SubVersion;
            result.ExtraVersion = versionModel.ExtraVersion;
            result.Postfix = versionModel.Postfix;
            return result;
        }

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

        #endregion

        #region Convert to history items

        public static SensorHistoryMessage Convert(SensorDataObject dataObject)
        {
            SensorHistoryMessage result = new SensorHistoryMessage();
            try
            {
                result.TypedData = dataObject.TypedData;
                result.Time = Timestamp.FromDateTime(dataObject.Time.ToUniversalTime());
                result.Type = Convert(dataObject.DataType);
            }
            catch (Exception e)
            {
                
            }
            return result;
        }

        #endregion
        #region Convert to database objects

        private static void FillCommonFields(SensorValueBase value, DateTime timeCollected, out SensorDataObject dataObject)
        {
            dataObject = new SensorDataObject();
            dataObject.Path = value.Path;
            dataObject.Time = value.Time;
            dataObject.TimeCollected = timeCollected;
            dataObject.Timestamp = GetTimestamp(value.Time);
        }

        private static SensorType Convert(SensorObjectType type)
        {
            switch (type)
            {
                case SensorObjectType.ObjectTypeBoolSensor:
                    return SensorType.BooleanSensor;
                case SensorObjectType.ObjectTypeDoubleSensor:
                    return SensorType.DoubleSensor;
                case SensorObjectType.ObjectTypeIntSensor:
                    return SensorType.IntSensor;
                case SensorObjectType.ObjectTypeStringSensor:
                    return SensorType.StringSensor;
                case SensorObjectType.ObjectTypeBarDoubleSensor:
                    return SensorType.DoubleBarSensor;
                case SensorObjectType.ObjectTypeBarIntSensor:
                    return SensorType.IntegerBarSensor;
                default:
                    throw new InvalidEnumArgumentException($"Invalid SensorDataType: {type}");
            }
        }
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

            BoolSensorData typedData = new BoolSensorData() { BoolValue = sensorValue.BoolValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(IntSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = SensorType.IntSensor;

            IntSensorData typedData = new IntSensorData() { IntValue = sensorValue.IntValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(DoubleSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = SensorType.DoubleSensor;

            DoubleSensorData typedData = new DoubleSensorData() { DoubleValue = sensorValue.DoubleValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(StringSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = SensorType.StringSensor;

            StringSensorData typedData = new StringSensorData() { StringValue = sensorValue.StringValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(IntBarSensorValue sensorValue, DateTime timeCollected)
        {
            FillCommonFields(sensorValue, timeCollected, out var result);
            result.DataType = SensorType.IntegerBarSensor;

            IntBarSensorData typedData = new IntBarSensorData()
            {
                Max = sensorValue.Max,
                Min = sensorValue.Min,
                Mean = sensorValue.Mean,
                Count = sensorValue.Count,
                Comment = sensorValue.Comment,
                StartTime = sensorValue.StartTime,
                EndTime = sensorValue.EndTime,
                Percentiles = sensorValue.Percentiles
            };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(DoubleBarSensorValue sensorValue, DateTime timeCollected)
        {
            SensorDataObject result;
            FillCommonFields(sensorValue, timeCollected, out result);
            result.DataType = SensorType.DoubleBarSensor;

            DoubleBarSensorData typedData = new DoubleBarSensorData()
            {
                Max = sensorValue.Max,
                Min = sensorValue.Min,
                Mean = sensorValue.Mean,
                Count = sensorValue.Count,
                Comment = sensorValue.Comment,
                StartTime = sensorValue.StartTime,
                EndTime = sensorValue.EndTime,
                Percentiles = sensorValue.Percentiles
            };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        #endregion


        #region Convert to update messages

        public static SensorUpdateMessage Convert(SensorDataObject dataObject, string productName)
        {
            SensorUpdateMessage result = new SensorUpdateMessage();
            result.Path = dataObject.Path;
            result.ObjectType = Convert(dataObject.DataType);
            result.Product = productName;
            result.Time = Timestamp.FromDateTime(dataObject.TimeCollected.ToUniversalTime());
            result.ShortValue = GetShortValue(dataObject.TypedData, dataObject.DataType, dataObject.TimeCollected);
            return result;
        }
        
        public static SensorUpdateMessage Convert(BoolSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            update.ShortValue = GetShortValue(value, timeCollected);
            update.ObjectType = SensorObjectType.ObjectTypeBoolSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }

        public static SensorUpdateMessage Convert(IntSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            update.ShortValue = GetShortValue(value, timeCollected);
            update.ObjectType = SensorObjectType.ObjectTypeIntSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }

        public static SensorUpdateMessage Convert(DoubleSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            update.ShortValue = GetShortValue(value, timeCollected);
            update.ObjectType = SensorObjectType.ObjectTypeDoubleSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }

        public static SensorUpdateMessage Convert(StringSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            update.ShortValue = GetShortValue(value, timeCollected);
            update.ObjectType = SensorObjectType.ObjectTypeStringSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }

        public static SensorUpdateMessage Convert(IntBarSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            update.ShortValue = GetShortValue(value, timeCollected);
            update.ObjectType = SensorObjectType.ObjectTypeBarIntSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }

        public static SensorUpdateMessage Convert(DoubleBarSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            update.ShortValue = GetShortValue(value, timeCollected);
            update.ObjectType = SensorObjectType.ObjectTypeBarDoubleSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }
        private static void AddCommonValues(SensorValueBase value, string productName, DateTime timeCollected, out SensorUpdateMessage update)
        {
            update = new SensorUpdateMessage();
            update.Path = value.Path;
            update.Product = productName;
            update.Time = Timestamp.FromDateTime(timeCollected.ToUniversalTime());
        }

        #endregion

        #region Typed data objects

        private static string GetShortValue(string stringData, SensorType sensorType, DateTime timeCollected)
        {
            try
            {
                switch (sensorType)
                {
                    case SensorType.BooleanSensor:
                        {
                            BoolSensorData boolData = JsonSerializer.Deserialize<BoolSensorData>(stringData);
                            return $"Time: {timeCollected:G}. Value = {boolData.BoolValue}";
                        }
                    case SensorType.IntSensor:
                        {
                            IntSensorData intData = JsonSerializer.Deserialize<IntSensorData>(stringData);
                            return $"Time: {timeCollected:G}. Value = {intData.IntValue}";
                        }
                    case SensorType.DoubleSensor:
                        {
                            DoubleSensorData doubleData = JsonSerializer.Deserialize<DoubleSensorData>(stringData);
                            return $"Time: {timeCollected:G}. Value = {doubleData.DoubleValue}";
                        }
                    case SensorType.StringSensor:
                        {
                            StringSensorData stringTypedData = JsonSerializer.Deserialize<StringSensorData>(stringData);
                            return $"Time: {timeCollected:G}. Value = '{stringTypedData.StringValue}'";
                        }
                    case SensorType.IntegerBarSensor:
                        {
                            IntBarSensorData intBarData = JsonSerializer.Deserialize<IntBarSensorData>(stringData);
                            return $"Time: {timeCollected:G}. Value: Min = {intBarData.Min}, Mean = {intBarData.Mean}, Max = {intBarData.Max}, Count = {intBarData.Count}";
                        }
                    case SensorType.DoubleBarSensor:
                        {
                            DoubleBarSensorData doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(stringData);
                            return $"Time: {timeCollected:G}. Value: Min = {doubleBarData.Min}, Mean = {doubleBarData.Mean}, Max = {doubleBarData.Max}, Count = {doubleBarData.Count}";
                        }
                    default:
                        throw new ApplicationException($"Unknown data type: {sensorType}!");
                }
            }
            catch (Exception e)
            {
                return string.Empty;
            }
        }
        private static string GetShortValue(BoolSensorValue value, DateTime timeCollected)
        {
            return $"Time: {timeCollected:G}. Value = {value.BoolValue}";
        }

        private static string GetShortValue(IntSensorValue value, DateTime timeCollected)
        {
            return $"Time: {timeCollected:G}. Value = {value.IntValue}";
        }

        private static string GetShortValue(DoubleSensorValue value, DateTime timeCollected)
        {
            return $"Time: {timeCollected:G}. Value = {value.DoubleValue}";
        }

        private static string GetShortValue(StringSensorValue value, DateTime timeCollected)
        {
            return $"Time: {timeCollected:G}. Value = {value.StringValue}";
        }

        private static string GetShortValue(IntBarSensorValue value, DateTime timeCollected)
        {
            return $"Time: {timeCollected:G}. Value: Min = {value.Min}, Mean = {value.Mean}, Max = {value.Max}, Count = {value.Count}";
        }

        private static string GetShortValue(DoubleBarSensorValue value, DateTime timeCollected)
        {
            return $"Time: {timeCollected:G}. Value: Min = {value.Min}, Mean = {value.Mean}, Max = {value.Max}, Count = {value.Count}";
        }
        #endregion

        public static ProductDataMessage Convert(Product product)
        {
            ProductDataMessage result = new ProductDataMessage();
            result.Name = product.Name;
            result.Key = product.Key;
            result.DateAdded = product.DateAdded.ToUniversalTime().ToTimestamp();
            return result;
        }

        public static GenerateClientCertificateModel Convert(CertificateRequestMessage requestMessage)
        {
            GenerateClientCertificateModel model = new GenerateClientCertificateModel
            {
                CommonName = requestMessage.CommonName,
                CountryName = requestMessage.CountryName,
                EmailAddress = requestMessage.EmailAddress,
                LocalityName = requestMessage.LocalityName,
                OrganizationName = requestMessage.OrganizationName,
                OrganizationUnitName = requestMessage.OrganizationUnitName,
                StateOrProvinceName = requestMessage.StateOrProvinceName
            };
            return model;
        }

        public static RSAParameters Convert(HSMService.RSAParameters rsaParameters)
        {
            RSAParameters result = new RSAParameters();
            result.D = rsaParameters.D.ToByteArray();
            result.DP = rsaParameters.DP.ToByteArray();
            result.DQ = rsaParameters.DQ.ToByteArray();
            result.Exponent = rsaParameters.Exponent.ToByteArray();
            result.InverseQ = rsaParameters.InverseQ.ToByteArray();
            result.Modulus = rsaParameters.Modulus.ToByteArray();
            result.P = rsaParameters.P.ToByteArray();
            result.Q = rsaParameters.Q.ToByteArray();
            return result;
        }

        #region Sub-methods

        private static SensorObjectType Convert(SensorType type)
        {
            //return (SensorObjectType) ((int) type);
            switch (type)
            {
                case SensorType.BooleanSensor:
                    return SensorObjectType.ObjectTypeBoolSensor;
                case SensorType.DoubleSensor:
                    return SensorObjectType.ObjectTypeDoubleSensor;
                case SensorType.IntSensor:
                    return SensorObjectType.ObjectTypeIntSensor;
                case SensorType.StringSensor:
                    return SensorObjectType.ObjectTypeStringSensor;
                case SensorType.IntegerBarSensor:
                    return SensorObjectType.ObjectTypeBarIntSensor;
                case SensorType.DoubleBarSensor:
                    return SensorObjectType.ObjectTypeBarDoubleSensor;
            }
            throw new Exception($"Unknown SensorDataType = {type}!");
        }
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
