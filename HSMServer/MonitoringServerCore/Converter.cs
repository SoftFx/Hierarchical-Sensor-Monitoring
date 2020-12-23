using System;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using HSMServer.DataLayer.Model;
using HSMServer.DataLayer.Model.TypedDataObjects;
using HSMServer.Model;
using NLog;
using SensorsService;
using RSAParameters = System.Security.Cryptography.RSAParameters;

namespace HSMServer.MonitoringServerCore
{
    public static class Converter
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();


        public static SignedCertificateMessage Convert(Org.BouncyCastle.X509.X509Certificate signedCert,
            X509Certificate2 caCertificate)
        {
            SignedCertificateMessage message = new SignedCertificateMessage();
            message.CaCertificateBytes = ByteString.CopyFrom(caCertificate.Export(X509ContentType.Cert));
            message.SignedCertificateBytes = ByteString.CopyFrom(signedCert.GetEncoded());
            return message;
        }

        public static SignedCertificateMessage Convert(X509Certificate2 signedCertificate,
            X509Certificate2 caCertificate)
        {
            SignedCertificateMessage message = new SignedCertificateMessage();
            message.CaCertificateBytes = ByteString.CopyFrom(caCertificate.Export(X509ContentType.Cert));
            message.SignedCertificateBytes = ByteString.CopyFrom(signedCertificate.Export(X509ContentType.Pfx));
            return message;
        }
        #region Convert to database objects

        public static SensorDataObject ConvertToDatabase(JobResult jobResult, DateTime timeCollected)
        {
            SensorDataObject result = new SensorDataObject();
            result.DataType = SensorDataTypes.JobSensor;
            result.Path = jobResult.Path;
            result.Time = jobResult.Time;
            result.Timestamp = GetTimestamp(result.Time);
            TypedJobSensorData typedData = new TypedJobSensorData { Success = jobResult.Success };
            result.TypedData = JsonSerializer.Serialize(typedData);
            result.TimeCollected = timeCollected;
            return result;
        }
        public static SensorDataObject ConvertToDatabase(NewJobResult newJobResult)
        {
            SensorDataObject result = new SensorDataObject();
            result.DataType = SensorDataTypes.JobSensor;
            result.Path = newJobResult.Path;
            result.Time = newJobResult.Time;
            result.Timestamp = GetTimestamp(newJobResult.Time);
            TypedJobSensorData typedData = new TypedJobSensorData { Success = newJobResult.Success };
            result.TypedData = JsonSerializer.Serialize(typedData);
            return result;
        }

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
            result.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeJobSensor;
            result.Path = newJobResult.Path;
            TypedJobSensorData typedData = new TypedJobSensorData { Success = newJobResult.Success };
            result.DataObject = ByteString.CopyFrom(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(typedData)));
            return result;
        }
        public static SensorUpdateMessage Convert(JobResult jobResult, string productName, DateTime timeCollected)
        {
            SensorUpdateMessage update = new SensorUpdateMessage();
            update.ObjectType = SensorUpdateMessage.Types.SensorObjectType.ObjectTypeJobSensor;
            update.Path = jobResult.Path;
            update.Product = productName;
            update.Name = ExtractSensor(jobResult.Path);
            TypedJobSensorData data = new TypedJobSensorData
            {
                Comment =  jobResult.Comment,
                Success =  jobResult.Success
            };
            update.DataObject = ByteString.CopyFrom(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(data)));
            update.Time = Timestamp.FromDateTime(timeCollected.ToUniversalTime());
            return update;
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
                case SensorDataTypes.JobSensor:
                    return SensorUpdateMessage.Types.SensorObjectType.ObjectTypeJobSensor;
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
