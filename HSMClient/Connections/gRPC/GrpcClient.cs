using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using HSMClient.Configuration;
using HSMClientWPFControls.Objects;
using SensorsService;

namespace HSMClient.Connections.gRPC
{
    public class GrpcClient : ConnectorBase
    {
        private readonly Sensors.SensorsClient _sensorsClient;
        public GrpcClient(string sensorsUrl) : base(sensorsUrl)
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ClientCertificates.Add(
                new X509Certificate2(Path.Combine(ConfigProvider.Instance.CertificatesFolderPath, "test.pfx")));
            handler.ServerCertificateCustomValidationCallback = ServerCertificateValidationCallback;


            var channel = GrpcChannel.ForAddress(sensorsUrl, new GrpcChannelOptions()
            {
                HttpHandler = handler
            });

            _sensorsClient = new Sensors.SensorsClient(channel);
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
            AddProductResultMessage message = _sensorsClient.AddNewProduct(new AddProductMessage {Name = name});
            if (!message.Result)
            {
                throw new Exception(message.Error);
            }

            return Converter.Convert(message.ProductData);
        }

        public override bool RemoveProduct(string name) 
        {
            RemoveProductResultMessage message = _sensorsClient.RemoveProduct(new RemoveProductMessage {Name = name});
            return message.Result;
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