using System;
using System.Threading.Tasks;
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
