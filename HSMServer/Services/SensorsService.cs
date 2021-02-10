using System;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HSMServer.Authentication;
using HSMServer.MonitoringServerCore;
using SensorsService;
using NLog;

namespace HSMServer.Services
{
    public class SensorsService : Sensors.SensorsBase
    {
        private readonly Logger _logger;
        private readonly IMonitoringCore _monitoringCore;
        private readonly UserManager _userManager;

        public SensorsService(IMonitoringCore monitoringCore, UserManager userManager)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _monitoringCore = monitoringCore;
            _userManager = userManager;
    
            _logger.Info("Sensors service started");
        }

        public override Task<SensorsUpdateMessage> GetMonitoringUpdates(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();

            //return Task.FromResult(_monitoringCore.GetSensorUpdates(httpContext.Connection.ClientCertificate));
            User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            return Task.FromResult(_monitoringCore.GetSensorUpdates(user));
        }

        public override Task<SensorsUpdateMessage> GetMonitoringTree(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);

            //return Task.FromResult(_monitoringCore.GetAllAvailableSensorsUpdates(httpContext.Connection.ClientCertificate));
            return Task.FromResult(_monitoringCore.GetSensorsTree(user));
        }

        public override Task<SensorHistoryListMessage> GetSensorHistory(GetSensorHistoryMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            //return Task.FromResult(_monitoringCore.GetSensorHistory(httpContext.Connection.ClientCertificate, request));
            return Task.FromResult(_monitoringCore.GetSensorHistory(user, request.Name, request.Path, request.Product, request.N));
        }

        public override Task<ProductsListMessage> GetProductsList(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            //return Task.FromResult(_monitoringCore.GetProductsList(httpContext.Connection.ClientCertificate));
            return Task.FromResult(_monitoringCore.GetProductsList(user));
        }

        public override Task<AddProductResultMessage> AddNewProduct(AddProductMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            //return Task.FromResult(_monitoringCore.AddNewProduct(httpContext.Connection.ClientCertificate, request));
            return Task.FromResult(_monitoringCore.AddProduct(user, request.Name));
        }

        public override Task<RemoveProductResultMessage> RemoveProduct(RemoveProductMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            //return Task.FromResult(_monitoringCore.RemoveProduct(httpContext.Connection.ClientCertificate, request));
            return Task.FromResult(_monitoringCore.RemoveProduct(user, request.Name));
        }

        public override Task<SignedCertificateMessage> SignClientCertificate(CertificateSignRequestMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();

            return Task.FromResult(
                _monitoringCore.SignClientCertificate(httpContext.Connection.ClientCertificate, request));
        }

        public override Task<GenerateServerCertificateResulMessage> GenerateServerCertificate(CertificateRequestMessage request, ServerCallContext context)
        {
            return base.GenerateServerCertificate(request, context);
        }

        public override Task<ServerAvailableMessage> CheckServerAvailable(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new ServerAvailableMessage() {Time = Timestamp.FromDateTime(DateTime.Now.ToUniversalTime())});
        }
    }
}
