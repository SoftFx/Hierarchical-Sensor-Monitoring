using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using HSMClient.Common.Logging;
using HSMClient.Configuration;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;
using SensorsService;
using HSMCommon.Certificates;
using HSMCommon.Model;

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
            if (_sensorsClient != null)
            {
                _sensorsClient = null;
            }
            HttpClientHandler handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ClientCertificates.Add(clientCertificate);
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
                Logger.Error($"CheckServerAvailable error = {e}");
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

        public override List<SensorHistoryItem> GetSensorHistory(string product, string path, string name, long n)
        {
            GetSensorHistoryMessage message = new GetSensorHistoryMessage { Product = product, Path = path, Name = name, N = n };
            SensorHistoryListMessage sensorHistoryList = _sensorsClient.GetSensorHistory(message);
            var convertedList = sensorHistoryList.Sensors.Select(Converter.Convert).ToList();
            convertedList.Sort((u1,u2) => u2.Time.CompareTo(u1.Time));
            return convertedList;
        }
        public override ClientVersionModel GetLastAvailableVersion()
        {
            try
            {
                ClientVersionMessage message = _sensorsClient.GetLastAvailableClientVersion(new Empty());
                return Converter.Convert(message);
            }
            catch (Exception e)
            {
                return new ClientVersionModel() {MainVersion = 0,SubVersion = 0,ExtraVersion = 0};
            }
        }
        public override X509Certificate2 GetSignedClientCertificate(CreateCertificateModel model,
            out X509Certificate2 caCertificate)
        {
            CertificateData data = Converter.Convert(model);
            string subjectString = CertificatesProcessor.GetSubjectString(data);
            var rsa = RSA.Create(2048);
            CertificateSignRequestMessage request = new CertificateSignRequestMessage();
            request.Subject = subjectString;
            request.RSAParameters = Converter.Convert(rsa.ExportParameters(true));
            request.CommonName = model.CommonName;
            //var certificateRequest = CertificatesProcessor.CreateCertificateSignRequest(data, out subjectKeyPair);
            var signedCertificateMessage = _sensorsClient.SignClientCertificate(request);
            caCertificate = new X509Certificate2(signedCertificateMessage.CaCertificateBytes.ToByteArray(), "",
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
            X509Certificate2 winCertificate = new X509Certificate2(signedCertificateMessage.SignedCertificateBytes.ToByteArray(), "",
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
            return winCertificate;
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
