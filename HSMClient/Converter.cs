using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Google.Protobuf;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;
using HSMCommon.Certificates;
using SensorsService;
using RSAParameters = SensorsService.RSAParameters;

namespace HSMClient
{
    public static class Converter
    {
        public static MonitoringSensorUpdate Convert(SensorUpdateMessage updateMessage)
        {
            MonitoringSensorUpdate result = new MonitoringSensorUpdate();
            result.Product = updateMessage.Product;
            result.ActionType = Convert(updateMessage.ActionType);
            result.Name = updateMessage.Name;
            result.Path = ConvertSensorPath(updateMessage.Path);
            result.SensorType = Convert(updateMessage.ObjectType);
            result.DataObject = updateMessage.DataObject.ToByteArray();
            result.Time = updateMessage.Time.ToDateTime();
            return result;
        }

        public static ProductInfo Convert(ProductDataMessage productData)
        {
            ProductInfo result = new ProductInfo();
            result.Name = productData.Name;
            result.Key = productData.Key;
            result.DateRegistered = productData.DateAdded.ToDateTime();
            return result;
        }

        private static ActionTypes Convert(SensorUpdateMessage.Types.TransactionType transactionType)
        {
            switch (transactionType)
            {
                case SensorUpdateMessage.Types.TransactionType.TransAdd:
                    return ActionTypes.Add;
                case SensorUpdateMessage.Types.TransactionType.TransNone:
                    return ActionTypes.None;
                case SensorUpdateMessage.Types.TransactionType.TransRemove:
                    return ActionTypes.Remove;
                case SensorUpdateMessage.Types.TransactionType.TransUpdate:
                    return ActionTypes.Update;
            }
            throw new Exception($"Unknown transaction type: {transactionType}!");
        }

        private static SensorTypes Convert(SensorUpdateMessage.Types.SensorObjectType type)
        {
            switch (type)
            {
                case SensorUpdateMessage.Types.SensorObjectType.ObjectTypeJobSensor:
                    return SensorTypes.JobSensor;
            }
            throw new Exception($"Unknown sensor type: {type}!");
        }

        public static List<string> ConvertSensorPath(string path)
        {
            return path.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        public static CertificateData Convert(CreateCertificateModel model)
        {
            CertificateData data = new CertificateData
            {
                CommonName = model.CommonName,
                OrganizationName = model.OrganizationName,
                StateOrProvinceName = model.StateOrProvinceName,
                LocalityName = model.LocalityName,
                OrganizationUnitName = model.OrganizationUnitName,
                EmailAddress = model.EmailAddress,
                CountryName = model.CountryName
            };
            return data;
        }

        public static RSAParameters Convert(System.Security.Cryptography.RSAParameters rsaParams)
        {
            RSAParameters convertedParams = new RSAParameters();
            convertedParams.D = ByteString.CopyFrom(rsaParams.D);
            convertedParams.DP = ByteString.CopyFrom(rsaParams.DP);
            convertedParams.DQ = ByteString.CopyFrom(rsaParams.DQ);
            convertedParams.Exponent = ByteString.CopyFrom(rsaParams.Exponent);
            convertedParams.InverseQ = ByteString.CopyFrom(rsaParams.InverseQ);
            convertedParams.Modulus = ByteString.CopyFrom(rsaParams.Modulus);
            convertedParams.P = ByteString.CopyFrom(rsaParams.P);
            convertedParams.Q = ByteString.CopyFrom(rsaParams.Q);
            return convertedParams;
        }
    }
}
