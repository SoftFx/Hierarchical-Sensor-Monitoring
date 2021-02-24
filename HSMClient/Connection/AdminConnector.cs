using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using HSMClient.Configuration;
using HSMClientWPFControls.Model;
using HSMService;

namespace HSMClient.Connection
{
    public class AdminConnector
    {
        private Admin.AdminClient _adminClient;
        private string _address;
        private bool _isInitialized = false;
        public AdminConnector(string address)
        {
            _address = address;
        }

        public void Initialize()
        {
            if (!_isInitialized)
            {
                InitializeConnector(_address, ConfigProvider.Instance.ConnectionInfo.ClientCertificate);
            }
        }
        private void InitializeConnector(string url, X509Certificate2 clientCertificate)
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ClientCertificates.Add(clientCertificate);
            handler.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12;
            handler.AllowAutoRedirect = true;
            handler.ServerCertificateCustomValidationCallback = delegate { return true;};

            var channel = GrpcChannel.ForAddress(url, new GrpcChannelOptions()
            {
                HttpHandler = handler,

            });

            _adminClient = new Admin.AdminClient(channel);
        }
        public ClientUpdateInfoModel GetUpdateInfo()
        {
            var updateMessage = _adminClient.GetUpdateInfo(new Empty());
            return new ClientUpdateInfoModel() {Files = updateMessage.Files.ToList(), Size = updateMessage.Size};
        }

        public async Task<byte[]> GetFile(string fileName)
        {
            var cts = new CancellationTokenSource();
            var request = new UpdateStreamRequestMessage() {FileName = fileName};
            using var stream = new MemoryStream();
            using (var streamingCall = _adminClient.GetUpdateStream(request))
            {
                try
                {
                    await foreach (var val in streamingCall.ResponseStream.ReadAllAsync(cts.Token))
                    {
                        byte[] currentBytes = val.BytesData.ToByteArray();
                        stream.Write(currentBytes, 0, currentBytes.Length);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }

            return stream.ToArray();
        }
    }
}