using System;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using HSMSensorDataObjects;
using HSMServer.DataLayer.Model;
using HSMServer.DataLayer.Model.TypedDataObjects;
using HSMServer.Model;
using NLog;
using SensorsService;
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
        #region Convert to database objects
        
        private static void FillCommonFields(SensorValueBase value, DateTime timeCollected, out SensorDataObject dataObject)
        {
            dataObject = new SensorDataObject();
            dataObject.Path = value.Path;
            dataObject.Time = value.Time;
            dataObject.TimeCollected = timeCollected;
            dataObject.Timestamp = GetTimestamp(value.Time);
        }

        private static SensorDataTypes Convert(SensorUpdateMessage.Types.SensorObjectType type)
        {
            switch (type)
            {
                case SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBoolSensor:
                    return SensorDataTypes.BoolSensor;
                case SensorUpdateMessage.Types.SensorObjectType.ObjectTypeDoubleSensor:
                    return SensorDataTypes.DoubleSensor;
                case SensorUpdateMessage.Types.SensorObjectType.ObjectTypeIntSensor:
                    return SensorDataTypes.IntSensor;
                case SensorUpdateMessage.Types.SensorObjectType.ObjectTypeStringSensor:
                    return SensorDataTypes.StringSensor;
                case SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBarDoubleSensor:
                    return SensorDataTypes.BarDoubleSensor;
                case SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBarIntSensor:
                    return SensorDataTypes.BarIntSensor;
                default:
                    throw new InvalidEnumArgumentException($"Invalid SensorDataType: {type}");
            }
        }
        public static SensorDataObject ConvertToDatabase(SensorUpdateMessage update, DateTime originalTime)
        {
            SensorDataObject result = new SensorDataObject();
            result.Path = update.Path;
            result.Time = originalTime;
            result.TypedData = update.DataObject.ToString(Encoding.UTF8);
            result.TimeCollected = update.Time.ToDateTime();
            result.Timestamp = GetTimestamp(result.TimeCollected);
            result.DataType = Convert(update.ObjectType);
            return result;
        }
        public static SensorDataObject ConvertToDatabase(JobResult jobResult, DateTime timeCollected)
        {
            SensorDataObject result = new SensorDataObject();
            result.DataType = SensorDataTypes.BoolSensor;
            result.Path = jobResult.Path;
            result.Time = jobResult.Time;
            result.Timestamp = GetTimestamp(result.Time);
            BoolSensorData typedData = new BoolSensorData { BoolValue = jobResult.Success };
            result.TypedData = JsonSerializer.Serialize(typedData);
            result.TimeCollected = timeCollected;
            return result;
        }

        public static SensorDataObject ConvertToDatabase(BoolSensorValue sensorValue, DateTime timeCollected)
        {
            SensorDataObject result;
            FillCommonFields(sensorValue, timeCollected, out result);
            result.DataType = SensorDataTypes.BoolSensor;

            BoolSensorData typedData = new BoolSensorData() {BoolValue = sensorValue.BoolValue, Comment = sensorValue.Comment};
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(IntSensorValue sensorValue, DateTime timeCollected)
        {
            SensorDataObject result;
            FillCommonFields(sensorValue, timeCollected, out result);
            result.DataType = SensorDataTypes.IntSensor;

            IntSensorData typedData = new IntSensorData() { IntValue = sensorValue.IntValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(DoubleSensorValue sensorValue, DateTime timeCollected)
        {
            SensorDataObject result;
            FillCommonFields(sensorValue, timeCollected, out result);
            result.DataType = SensorDataTypes.DoubleSensor;

            DoubleSensorData typedData = new DoubleSensorData() { DoubleValue = sensorValue.DoubleValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(StringSensorValue sensorValue, DateTime timeCollected)
        {
            SensorDataObject result;
            FillCommonFields(sensorValue, timeCollected, out result);
            result.DataType = SensorDataTypes.StringSensor;

            StringSensorData typedData = new StringSensorData() { StringValue = sensorValue.StringValue, Comment = sensorValue.Comment };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(IntBarSensorValue sensorValue, DateTime timeCollected)
        {
            SensorDataObject result;
            FillCommonFields(sensorValue, timeCollected, out result);
            result.DataType = SensorDataTypes.BarIntSensor;

            IntBarSensorData typedData = new IntBarSensorData()
            {
                Max = sensorValue.Max, Min = sensorValue.Min, Mean = sensorValue.Mean,
                Count = sensorValue.Count, Comment = sensorValue.Comment
            };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

        public static SensorDataObject ConvertToDatabase(DoubleBarSensorValue sensorValue, DateTime timeCollected)
        {
            SensorDataObject result;
            FillCommonFields(sensorValue, timeCollected, out result);
            result.DataType = SensorDataTypes.BarIntSensor;

            DoubleBarSensorData typedData = new DoubleBarSensorData()
            {
                Max = sensorValue.Max,
                Min = sensorValue.Min,
                Mean = sensorValue.Mean,
                Count = sensorValue.Count,
                Comment = sensorValue.Comment
            };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }
        //public static SensorDataObject ConvertToDatabase(NewJobResult newJobResult)
        //{
        //    SensorDataObject result = new SensorDataObject();
        //    result.DataType = SensorDataTypes.BoolSensor;
        //    result.Path = newJobResult.Path;
        //    result.Time = newJobResult.Time;
        //    result.Timestamp = GetTimestamp(newJobResult.Time);
        //    BoolSensorData typedData = new BoolSensorData { BoolValue = newJobResult.Success };
        //    result.TypedData = JsonSerializer.Serialize(typedData);
        //    return result;
        //}

        public static SensorInfo ConvertToInfo(NewJobResult newJobResult)
        {
            SensorInfo result = new SensorInfo();
            result.Path = newJobResult.Path;
            result.ProductName = newJobResult.ProductName;
            result.SensorName = newJobResult.SensorName;
            return result;
        }

        #endregion


        #region Convert to update messages

        public static SensorUpdateMessage Convert(SensorDataObject dataObject, string productName)
        {
            SensorUpdateMessage result = new SensorUpdateMessage();
            result.Path = dataObject.Path;
            result.ObjectType = Convert(dataObject.DataType);
            result.DataObject = ByteString.CopyFrom(Encoding.ASCII.GetBytes(dataObject.TypedData));
            result.Name = ExtractSensor(dataObject.Path);
            result.Product = productName;
            result.Time = Timestamp.FromDateTime(dataObject.TimeCollected.ToUniversalTime());
            return result;
        }
        public static SensorUpdateMessage Convert(NewJobResult newJobResult)
        {
            SensorUpdateMessage result = new SensorUpdateMessage();
            result.Product = newJobResult.ProductName;
            result.Name = newJobResult.SensorName;
            result.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBoolSensor;
            result.Path = newJobResult.Path;
            BoolSensorData typedData = new BoolSensorData { BoolValue = newJobResult.Success };
            result.DataObject = ByteString.CopyFrom(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(typedData)));
            return result;
        }
        public static SensorUpdateMessage Convert(JobResult jobResult, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update = new SensorUpdateMessage();
            update.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBoolSensor;
            update.Path = jobResult.Path;
            update.Product = productName;
            update.Name = ExtractSensor(jobResult.Path);
            BoolSensorData data = new BoolSensorData
            {
                Comment =  jobResult.Comment,
                BoolValue =  jobResult.Success
            };
            update.DataObject = ByteString.CopyFrom(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(data)));
            update.Time = Timestamp.FromDateTime(timeCollected.ToUniversalTime());
            return update;
        }

        public static SensorUpdateMessage Convert(BoolSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            BoolSensorData data = new BoolSensorData
            {
                Comment = value.Comment,
                BoolValue = value.BoolValue
            };
            update.DataObject = ByteString.CopyFrom(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
            update.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBoolSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }

        public static SensorUpdateMessage Convert(IntSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            IntSensorData data = new IntSensorData()
            {
                Comment = value.Comment,
                IntValue = value.IntValue
            };
            update.DataObject = ByteString.CopyFrom(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
            update.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeIntSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }

        public static SensorUpdateMessage Convert(DoubleSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            DoubleSensorData data = new DoubleSensorData()
            {
                Comment = value.Comment,
                DoubleValue = value.DoubleValue
            };
            update.DataObject = ByteString.CopyFrom(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
            update.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeDoubleSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }

        public static SensorUpdateMessage Convert(StringSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            StringSensorData data = new StringSensorData()
            {
                Comment = value.Comment,
                StringValue = value.StringValue
            };
            update.DataObject = ByteString.CopyFrom(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
            update.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeStringSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }

        public static SensorUpdateMessage Convert(IntBarSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            IntBarSensorData data = new IntBarSensorData()
            {
                Comment = value.Comment,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                Count = value.Count
            };
            update.DataObject = ByteString.CopyFrom(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
            update.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBarIntSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }

        public static SensorUpdateMessage Convert(DoubleBarSensorValue value, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update;
            AddCommonValues(value, productName, timeCollected, out update);
            DoubleBarSensorData data = new DoubleBarSensorData()
            {
                Comment = value.Comment,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                Count = value.Count
            };
            update.DataObject = ByteString.CopyFrom(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data)));
            update.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBarDoubleSensor;
            update.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;

            return update;
        }
        private static void AddCommonValues(SensorValueBase value, string productName, DateTime timeCollected, out SensorUpdateMessage update)
        {
            update = new SensorUpdateMessage();
            update.Path = value.Path;
            update.Product = productName;
            update.Name = ExtractSensor(value.Path);
            update.Time = Timestamp.FromDateTime(timeCollected.ToUniversalTime());
        }

        #endregion

        #region Typed data objects



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

        public static RSAParameters Convert(SensorsService.RSAParameters rsaParameters)
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

        private static SensorUpdateMessage.Types.SensorObjectType Convert(SensorDataTypes type)
        {
            switch (type)
            {
                case SensorDataTypes.BoolSensor:
                    return SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBoolSensor;
                case SensorDataTypes.DoubleSensor:
                    return SensorUpdateMessage.Types.SensorObjectType.ObjectTypeDoubleSensor;
                case SensorDataTypes.IntSensor:
                    return SensorUpdateMessage.Types.SensorObjectType.ObjectTypeIntSensor;
                case SensorDataTypes.StringSensor:
                    return SensorUpdateMessage.Types.SensorObjectType.ObjectTypeStringSensor;
                case SensorDataTypes.BarIntSensor:
                    return SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBarIntSensor;
                case SensorDataTypes.BarDoubleSensor:
                    return SensorUpdateMessage.Types.SensorObjectType.ObjectTypeBarDoubleSensor;
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
