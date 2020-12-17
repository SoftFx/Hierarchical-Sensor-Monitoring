using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using HSMClient.Configuration;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;
using SensorsService;
using HSMCommon.Certificates;
using Org.BouncyCastle.Crypto;

namespace HSMClient.Connection
{
    public class GrpcClientConnector : ConnectorBase
    {
        private Sensors.SensorsClient _sensorsClient;
        public GrpcClientConnector(string sensorsUrl) : base(sensorsUrl)
        {
            AppContext.SetSwitch(
                "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            InitializeSensorsClient(sensorsUrl, ConfigProvider.Instance.ConnectionInfo.ClientCertificate);
        }

        private void InitializeSensorsClient(string sensorsUrl, X509Certificate2 clientCertificate)
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ClientCertificates.Add(new X509Certificate2(clientCertificate));
            handler.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
            handler.AllowAutoRedirect = true;
            handler.ServerCertificateCustomValidationCallback = ServerCertificateValidationCallback;

            var channel = GrpcChannel.ForAddress(sensorsUrl, new GrpcChannelOptions()
            {
                HttpHandler = handler,
                
            });

            _sensorsClient = new Sensors.SensorsClient(channel);
        }
        public override bool CheckServerAvailable()
        {
            try
            {
                var time = _sensorsClient.CheckServerAvailable(new Empty()).Time.ToDateTime();
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
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

        public override Org.BouncyCastle.X509.X509Certificate GetSignedClientCertificate(CreateCertificateModel model, 
            out AsymmetricCipherKeyPair subjectKeyPair,
            out X509Certificate2 caCertificate)
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
            var certificateRequest = CertificatesProcessor.CreateCertificateSignRequest(data, out subjectKeyPair);
            CertificateSignRequestMessage request = new CertificateSignRequestMessage();
            request.RequestBytes = ByteString.CopyFrom(certificateRequest.GetDerEncoded());
            request.CommonName = model.CommonName;
            var signedCertificateMessage = _sensorsClient.SignClientCertificate(request);
            caCertificate = new X509Certificate2(signedCertificateMessage.CaCertificateBytes.ToByteArray(), "",
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
            X509Certificate2 winCertificate = new X509Certificate2(signedCertificateMessage.SignedCertificateBytes.ToByteArray(), "",
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
            Org.BouncyCastle.X509.X509Certificate certificate = Org.BouncyCastle.Security.DotNetUtilities.FromX509Certificate(winCertificate);
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
