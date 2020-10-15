using System;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using HSMClient.Configuration;
using SensorsService;

namespace HSMClient.Connections.gRPC
{
    public class SensorsClient : ConnectorBase
    {
        private readonly Sensors.SensorsClient _sensorsClient;
        private readonly string _sensorName;
        private readonly string _machineName;
        public SensorsClient(string sensorsUrl, string sensorName, string machineName) : base(sensorsUrl)
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

            _machineName = machineName;
            _sensorName = sensorName;
        }

        public override object GetTree()
        {
            SensorsUpdateMessage updatesList = _sensorsClient.GetMonitoringTree(new Empty());
            return updatesList;
        }
        public override object GetUpdates()
        {
            SensorsUpdateMessage updatesList = _sensorsClient.GetMonitoringUpdates(new Empty());
            return updatesList;
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