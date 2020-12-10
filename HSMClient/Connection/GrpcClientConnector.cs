using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using HSMClient.Configuration;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;
using SensorsService;

namespace HSMClient.Connection
{
    public class GrpcClientConnector : ConnectorBase
    {
        private Sensors.SensorsClient _sensorsClient;
        public GrpcClientConnector(string sensorsUrl) : base(sensorsUrl)
        {
            InitializeSensorsClient(sensorsUrl, ConfigProvider.Instance.ConnectionInfo.ClientCertificate);
        }

        private void InitializeSensorsClient(string sensorsUrl, X509Certificate2 clientCertificate)
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ClientCertificates.Add(new X509Certificate2(clientCertificate));
            handler.ServerCertificateCustomValidationCallback = ServerCertificateValidationCallback;

            var channel = GrpcChannel.ForAddress(sensorsUrl, new GrpcChannelOptions()
            {
                HttpHandler = handler
            });

            _sensorsClient = new Sensors.SensorsClient(channel);
        }
        public override DateTime CheckServerAvailable()
        {
            return _sensorsClient.CheckServerAvailable(new Empty()).Time.ToDateTime();
        }

        public override List<MonitoringSensorUpdate> GetTree()
        {
            SensorsUpdateMessage updatesList = _sensorsClient.GetMonitoringTree(new Empty());
            return updatesList.Sensors.Select(Converter.Convert).ToList();
        }
        public override List<MonitoringSensorUpdate> GetUpdates()
        {
            SensorsUpdateMessage updatesList = _sensorsClient.GetMonitoringUpdates(new Empty());
            return updatesList.Sensors.Select(Converter.Convert).ToList();
        }
        public override List<ProductInfo> GetProductsList()
        {
            ProductsListMessage productsList = _sensorsClient.GetProductsList(new Empty());
            return productsList.Products.Select(Converter.Convert).ToList();
        }
        public override ProductInfo AddNewProduct(string name)
        {
            AddProductResultMessage message = _sensorsClient.AddNewProduct(new AddProductMessage { Name = name });
            if (!message.Result)
            {
                throw new Exception(message.Error);
            }

            return Converter.Convert(message.ProductData);
        }

        public override bool RemoveProduct(string name)
        {
            RemoveProductResultMessage message = _sensorsClient.RemoveProduct(new RemoveProductMessage { Name = name });
            return message.Result;
        }

        public override List<MonitoringSensorUpdate> GetSensorHistory(string product, string name, long n)
        {
            GetSensorHistoryMessage message = new GetSensorHistoryMessage { Product = product, Name = name, N = n };
            SensorsUpdateMessage sensorsUpdate = _sensorsClient.GetSensorHistory(message);
            var convertedList = sensorsUpdate.Sensors.Select(Converter.Convert).ToList();
            convertedList.Sort((u1,u2) => u2.Time.CompareTo(u1.Time));
            return convertedList;
        }

        public override X509Certificate2 GetNewClientCertificate(CreateCertificateModel model)
        {
            CertificateRequestMessage message = new CertificateRequestMessage
            {
                CommonName = model.CommonName,
                CountryName = model.CountryName,
                EmailAddress = model.EmailAddress,
                LocalityName = model.LocalityName,
                OrganizationUnitName = model.OrganizationUnitName,
                OrganizationName = model.OrganizationName,
                StateOrProvinceName = model.StateOrProvinceName,
            };
            var newCertificateBytes = _sensorsClient.GenerateClientCertificate(message);
            var type = X509Certificate2.GetCertContentType(newCertificateBytes.CertificateBytes.ToByteArray());
            X509Certificate2 certificate = new X509Certificate2(newCertificateBytes.CertificateBytes.ToByteArray(), "", 
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
            return certificate;
        }

        public override void ReplaceClientCertificate(X509Certificate2 certificate)
        {
            InitializeSensorsClient(_address, certificate);
        }

        private static bool ValidateServerCertificate(Object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        private static bool ServerCertificateValidationCallback(HttpRequestMessage message,
            X509Certificate2 certificate, X509Chain chain, SslPolicyErrors errors)
        {
            if (DateTime.Now > certificate.NotAfter || DateTime.Now < certificate.NotBefore)
            {
                return false;
            }


            return true;
        }
    }
}
