using System.Security.Cryptography.X509Certificates;
using HSMServer.Model;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public interface IMonitoringCore
    {
        public void AddSensorInfo(JobResult info);
        //public string AddSensorInfo(NewJobResult info);
        public SensorsUpdateMessage GetSensorUpdates(X509Certificate2 clientCertificate);
        public SensorsUpdateMessage GetAllAvailableSensorsUpdates(X509Certificate2 clientCertificate);
        public ProductsListMessage GetProductsList(X509Certificate2 clientCertificate);
        public AddProductResultMessage AddNewProduct(X509Certificate2 clientCertificate, AddProductMessage message);
        public RemoveProductResultMessage RemoveProduct(X509Certificate2 clientCertificate,
            RemoveProductMessage message);
        public SensorsUpdateMessage GetSensorHistory(X509Certificate2 clientCertificate, GetSensorHistoryMessage getHistoryMessage);

        public SignedCertificateMessage SignClientCertificate(X509Certificate2 clientCertificate,
            CertificateSignRequestMessage request);
    }
}
