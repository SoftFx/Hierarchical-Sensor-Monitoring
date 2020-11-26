using HSMServer.Model;
using Microsoft.AspNetCore.Http;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public interface IMonitoringCore
    {
        public void AddSensorInfo(JobResult info);
        //public string AddSensorInfo(NewJobResult info);
        public SensorsUpdateMessage GetSensorUpdates(ConnectionInfo connection);
        public SensorsUpdateMessage GetAllAvailableSensorsUpdates(ConnectionInfo connection);
        public ProductsListMessage GetProductsList(ConnectionInfo connection);
        public AddProductResultMessage AddNewProduct(ConnectionInfo connection, AddProductMessage message);
        public RemoveProductResultMessage RemoveProduct(ConnectionInfo connection,
            RemoveProductMessage message);
        public SensorsUpdateMessage GetSensorHistory(ConnectionInfo connection, GetSensorHistoryMessage getHistoryMessage);
    }
}
