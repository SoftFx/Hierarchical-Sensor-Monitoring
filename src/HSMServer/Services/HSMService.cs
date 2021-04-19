using System;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HSMServer.Authentication;
using HSMServer.MonitoringServerCore;
using HSMService;
using NLog;

namespace HSMServer.Services
{
    public class HSMService : Sensors.SensorsBase
    {
        private readonly Logger _logger;
        private readonly IMonitoringCore _monitoringCore;
        private const int BLOCK_SIZE = 1048576;
        public HSMService(IMonitoringCore monitoringCore)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _monitoringCore = monitoringCore;

            _logger.Info("Sensors service started");
        }

        public override Task<SensorsUpdateMessage> GetMonitoringUpdates(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            return Task.FromResult(_monitoringCore.GetSensorUpdates(httpContext.User as User));
        }

        public override Task<SensorsUpdateMessage> GetMonitoringTree(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            return Task.FromResult(_monitoringCore.GetSensorsTree(httpContext.User as User));
        }

        public override Task<SensorHistoryListMessage> GetSensorHistory(GetSensorHistoryMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            return Task.FromResult(_monitoringCore.GetSensorHistory(httpContext.User as User, request.Name, request.Path, request.Product, request.N));
        }

        public override async Task GetFileSensorStream(GetFileSensorValueMessage request, IServerStreamWriter<FileStreamMessage> responseStream,
            ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();

            var sensorValue = _monitoringCore.GetFileSensorValue(httpContext.User as User, request.Product, request.Path);
            byte[] bytes = Encoding.UTF8.GetBytes(sensorValue);
            int count = 0;
            int currentIndex = 0;
            int bytesLeft = bytes.Length;
            while (currentIndex < bytesLeft)
            {
                FileStreamMessage message = new FileStreamMessage();
                if (bytesLeft <= BLOCK_SIZE)
                {
                    message.BytesData = ByteString.CopyFrom(bytes);
                    message.BlockSize = bytesLeft;
                    message.BlockIndex = count;
                    currentIndex = bytesLeft;
                }
                else
                {
                    message.BytesData = ByteString.CopyFrom(bytes[currentIndex..(BLOCK_SIZE + currentIndex)]);
                    message.BlockIndex = count;
                    message.BlockSize = BLOCK_SIZE;
                    bytesLeft = bytesLeft - BLOCK_SIZE;
                    currentIndex = currentIndex + BLOCK_SIZE;
                }

                await responseStream.WriteAsync(message);

                ++count;
            }
        }

        public override Task<StringMessage> GetFileSensorExtension(GetFileSensorValueMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            return Task.FromResult(
                _monitoringCore.GetFileSensorValueExtension(httpContext.User as User, request.Product, request.Path));
        }

        public override Task<ProductsListMessage> GetProductsList(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            return Task.FromResult(_monitoringCore.GetProductsList(httpContext.User as User));
        }

        public override Task<AddProductResultMessage> AddNewProduct(AddProductMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            return Task.FromResult(_monitoringCore.AddProduct(httpContext.User as User, request.Name));
        }

        public override Task<RemoveProductResultMessage> RemoveProduct(RemoveProductMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            return Task.FromResult(_monitoringCore.RemoveProduct(httpContext.User as User, request.Name));
        }

        public override Task<SignedCertificateMessage> SignClientCertificate(CertificateSignRequestMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();

            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate
            //    .Thumbprint);
            return Task.FromResult(_monitoringCore.SignClientCertificate(httpContext.User as User, request));
        }

        public override Task<ServerAvailableMessage> CheckServerAvailable(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new ServerAvailableMessage() {Time = Timestamp.FromDateTime(DateTime.Now.ToUniversalTime())});
        }

        public override Task<ClientVersionMessage> GetLastAvailableClientVersion(Empty request, ServerCallContext context)
        {
            return Task.FromResult(_monitoringCore.GetLastAvailableClientVersion());
        }
    }
}
