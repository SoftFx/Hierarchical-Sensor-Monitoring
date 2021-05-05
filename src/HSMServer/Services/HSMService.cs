using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using HSMCommon.Model;
using HSMSensorDataObjects;
using HSMServer.Authentication;
using HSMServer.DataLayer.Model;
using HSMServer.Model;
using HSMServer.Model.SensorsData;
using HSMServer.MonitoringServerCore;
using HSMService;
using NLog;
using SensorStatus = HSMService.SensorStatus;

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
            var updatesList = _monitoringCore.GetSensorUpdates(httpContext.User as User);
            return Task.FromResult(Convert(updatesList));
        }

        public override Task<SensorsUpdateMessage> GetMonitoringTree(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            var treeList = _monitoringCore.GetSensorsTree(httpContext.User as User);
            return Task.FromResult(Convert(treeList));
        }

        public override Task<SensorHistoryListMessage> GetSensorHistory(GetSensorHistoryMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            var historyList = _monitoringCore.GetSensorHistory(httpContext.User as User, request.Path, request.Product,
                request.N);
            return Task.FromResult(Convert(historyList));
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

            string extension =
                _monitoringCore.GetFileSensorValueExtension(httpContext.User as User, request.Product, request.Path);
            return Task.FromResult(Convert(extension));
        }

        public override Task<ProductsListMessage> GetProductsList(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            var list = _monitoringCore.GetProductsList(httpContext.User as User);
            return Task.FromResult(Convert(list));
        }

        public override Task<AddProductResultMessage> AddNewProduct(AddProductMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            bool result =
                _monitoringCore.AddProduct(httpContext.User as User, request.Name, out var product, out var error);
            return Task.FromResult(GetAddResultMessage(product, result, error));
        }

        public override Task<RemoveProductResultMessage> RemoveProduct(RemoveProductMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate.Thumbprint);
            bool result = _monitoringCore.RemoveProduct(httpContext.User as User, request.Name, out var product,
                out var error);
            return Task.FromResult(GetRemoveResultMessage(product, result, error));
        }

        public override Task<SignedCertificateMessage> SignClientCertificate(CertificateSignRequestMessage request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();

            //User user = _userManager.GetUserByCertificateThumbprint(httpContext.Connection.ClientCertificate
            //    .Thumbprint);
            var certs = _monitoringCore.SignClientCertificate(httpContext.User as User, request.Subject,
                request.CommonName, Convert(request.RSAParameters));
            return Task.FromResult(Convert(certs.Item1, certs.Item2));
        }

        public override Task<ServerAvailableMessage> CheckServerAvailable(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new ServerAvailableMessage() {Time = Timestamp.FromDateTime(DateTime.Now.ToUniversalTime())});
        }

        public override Task<ClientVersionMessage> GetLastAvailableClientVersion(Empty request, ServerCallContext context)
        {
            return Task.FromResult(Convert(_monitoringCore.GetLastAvailableClientVersion()));
        }

        #region Convert objects to messages

        #region Sensors

        private SensorHistoryListMessage Convert(List<SensorHistoryData> historyList)
        {
            SensorHistoryListMessage result = new SensorHistoryListMessage();
            result.Sensors.AddRange(historyList.Select(Convert));
            return result;
        }
        private SensorsUpdateMessage Convert(List<SensorData> list)
        {
            SensorsUpdateMessage result = new SensorsUpdateMessage();
            result.Sensors.AddRange(list.Select(Convert));
            return result;
        }

        private SensorUpdateMessage Convert(SensorData data)
        {
            SensorUpdateMessage result = new SensorUpdateMessage();
            result.Status = Convert(data.Status);
            result.Time = Timestamp.FromDateTime(data.Time.ToUniversalTime());
            result.ShortValue = data.ShortValue;
            result.Product = data.Product;
            result.ObjectType = Convert(data.SensorType);
            result.Path = data.Path;
            result.ActionType = SensorUpdateMessage.Types.TransactionType.TransAdd;
            return result;
        }

        private SensorHistoryMessage Convert(SensorHistoryData data)
        {
            SensorHistoryMessage result = new SensorHistoryMessage();
            result.TypedData = data.TypedData;
            result.Time = Timestamp.FromDateTime(data.Time.ToUniversalTime());
            result.Type = Convert(data.SensorType);
            return result;
        }
        private SensorStatus Convert(HSMSensorDataObjects.SensorStatus status)
        {
            switch (status)
            {
                case HSMSensorDataObjects.SensorStatus.Unknown:
                    return SensorStatus.Unknown;
                case HSMSensorDataObjects.SensorStatus.Ok:
                    return SensorStatus.Ok;
                case HSMSensorDataObjects.SensorStatus.Warning:
                    return SensorStatus.Warning;
                case HSMSensorDataObjects.SensorStatus.Error:
                    return SensorStatus.Error;
                default:
                    throw new Exception($"Unknown sensor status: {status}!");
            }
        }

        private SensorObjectType Convert(SensorType type)
        {
            //return (SensorObjectType) ((int) type);
            switch (type)
            {
                case SensorType.BooleanSensor:
                    return SensorObjectType.ObjectTypeBoolSensor;
                case SensorType.DoubleSensor:
                    return SensorObjectType.ObjectTypeDoubleSensor;
                case SensorType.IntSensor:
                    return SensorObjectType.ObjectTypeIntSensor;
                case SensorType.StringSensor:
                    return SensorObjectType.ObjectTypeStringSensor;
                case SensorType.IntegerBarSensor:
                    return SensorObjectType.ObjectTypeBarIntSensor;
                case SensorType.DoubleBarSensor:
                    return SensorObjectType.ObjectTypeBarDoubleSensor;
                case SensorType.FileSensor:
                    return SensorObjectType.ObjectTypeFileSensor;
            }
            throw new Exception($"Unknown SensorDataType = {type}!");
        }
        #endregion

        #region Products

        private ProductsListMessage Convert(List<Product> products)
        {
            ProductsListMessage result = new ProductsListMessage();
            result.Products.AddRange(products.Select(Convert));
            return result;
        }
        private ProductDataMessage Convert(Product product)
        {
            ProductDataMessage result = new ProductDataMessage();
            result.Name = product.Name;
            result.Key = product.Key;
            result.DateAdded = product.DateAdded.ToUniversalTime().ToTimestamp();
            return result;
        }

        private AddProductResultMessage GetAddResultMessage(Product product, bool success, string error)
        {
            AddProductResultMessage result = new AddProductResultMessage();
            result.Result = success;
            result.ProductData = Convert(product);
            result.Error = error;
            return result;
        }

        private RemoveProductResultMessage GetRemoveResultMessage(Product product, bool success, string error)
        {
            RemoveProductResultMessage result = new RemoveProductResultMessage();
            result.Result = success;
            result.ProductData = Convert(product);
            result.Error = error;
            return result;
        }

        #endregion
        private ClientVersionMessage Convert(ClientVersionModel versionModel)
        {
            ClientVersionMessage result = new ClientVersionMessage();
            result.MainVersion = versionModel.MainVersion;
            result.SubVersion = versionModel.SubVersion;
            result.ExtraVersion = versionModel.ExtraVersion;
            result.Postfix = versionModel.Postfix;
            return result;
        }

        private SignedCertificateMessage Convert(X509Certificate2 signedCertificate,
            X509Certificate2 caCertificate)
        {
            SignedCertificateMessage message = new SignedCertificateMessage();
            message.CaCertificateBytes = ByteString.CopyFrom(caCertificate.Export(X509ContentType.Cert));
            message.SignedCertificateBytes = ByteString.CopyFrom(signedCertificate.Export(X509ContentType.Pfx));
            return message;
        }

        private System.Security.Cryptography.RSAParameters Convert(RSAParameters rsaParameters)
        {
            System.Security.Cryptography.RSAParameters result = new System.Security.Cryptography.RSAParameters();
            result.D = rsaParameters.D.ToByteArray();
            result.DP = rsaParameters.DP.ToByteArray();
            result.DQ = rsaParameters.DQ.ToByteArray();
            result.Exponent = rsaParameters.Exponent.ToByteArray();
            result.InverseQ = rsaParameters.InverseQ.ToByteArray();
            result.Modulus = rsaParameters.Modulus.ToByteArray();
            result.P = rsaParameters.P.ToByteArray();
            result.Q = rsaParameters.Q.ToByteArray();
            return result;
        }
        
        public GenerateClientCertificateModel Convert(CertificateRequestMessage requestMessage)
        {
            GenerateClientCertificateModel model = new GenerateClientCertificateModel
            {
                CommonName = requestMessage.CommonName,
                CountryName = requestMessage.CountryName,
                EmailAddress = requestMessage.EmailAddress,
                LocalityName = requestMessage.LocalityName,
                OrganizationName = requestMessage.OrganizationName,
                OrganizationUnitName = requestMessage.OrganizationUnitName,
                StateOrProvinceName = requestMessage.StateOrProvinceName
            };
            return model;
        }

        public StringMessage Convert(string value)
        {
            StringMessage result = new StringMessage();
            result.Data = value;
            return result;
        }
        #endregion
    }
}
