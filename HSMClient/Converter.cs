using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;
using HSMCommon.Certificates;
using HSMCommon.Model;
using SensorsService;
using RSAParameters = SensorsService.RSAParameters;

namespace HSMClient
{
    public static class Converter
    {
        public static ClientVersionModel Convert(ClientVersionMessage versionMessage)
        {
            ClientVersionModel result = new ClientVersionModel();
            result.ExtraVersion = versionMessage.ExtraVersion;
            result.MainVersion = versionMessage.MainVersion;
            result.SubVersion = versionMessage.SubVersion;
            result.Postfix = versionMessage.Postfix;
            return result;
        }
        public static SensorHistoryItem Convert(SensorHistoryMessage historyMessage)
        {
            SensorHistoryItem result = new SensorHistoryItem();
            result.Time = historyMessage.Time.ToDateTime();
            result.Type = Convert(historyMessage.Type);
            result.SensorValue = historyMessage.TypedData;
            return result;
        }
        public static MonitoringSensorUpdate Convert(SensorUpdateMessage updateMessage)
        {
            MonitoringSensorUpdate result = new MonitoringSensorUpdate();
            result.Product = updateMessage.Product;
            result.ActionType = Convert(updateMessage.ActionType);
            result.Path = ConvertSensorPath(updateMessage.Path, updateMessage.Product);
            result.Name = result.Path[^1];
            result.SensorType = Convert(updateMessage.ObjectType);
            result.Time = updateMessage.Time.ToDateTime();
            result.ShortValue = updateMessage.ShortValue;
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

        private static SensorTypes Convert(SensorObjectType type)
        {
            switch (type)
            {
                case SensorObjectType.ObjectTypeBoolSensor:
                    return SensorTypes.BoolSensor;
                case SensorObjectType.ObjectTypeIntSensor:
                    return SensorTypes.IntSensor;
                case SensorObjectType.ObjectTypeDoubleSensor:
                    return SensorTypes.DoubleSensor;
                case SensorObjectType.ObjectTypeStringSensor:
                    return SensorTypes.StringSensor;
                case SensorObjectType.ObjectTypeBarDoubleSensor:
                    return SensorTypes.BarDoubleSensor;
                case SensorObjectType.ObjectTypeBarIntSensor:
                    return SensorTypes.BarIntSensor;
                default:
                    return SensorTypes.None;
            }
            throw new Exception($"Unknown sensor type: {type}!");
        }

        public static List<string> ConvertSensorPath(string path, string productName)
        {
            //var list = new List<string>();
            //list.Add(productName);
            //list.AddRange(path.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries));
            //return list;
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
