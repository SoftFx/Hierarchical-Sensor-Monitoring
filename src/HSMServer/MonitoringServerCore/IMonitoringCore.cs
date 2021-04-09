using System.Collections.Generic;
using System.Threading.Tasks;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Authentication;
using HSMService;

namespace HSMServer.MonitoringServerCore
{
    public interface IMonitoringCore
    {
        //public void AddSensorValue(JobResult value);
        public void AddSensorsValues(IEnumerable<CommonSensorValue> values);
        Task<bool> AddSensorValueAsync(BoolSensorValue value);
        public void AddSensorValue(BoolSensorValue value);
        public void AddSensorValue(IntSensorValue value);
        public void AddSensorValue(DoubleSensorValue value);
        public void AddSensorValue(StringSensorValue value);
        public void AddSensorValue(IntBarSensorValue value);
        public void AddSensorValue(DoubleBarSensorValue value);

        public void AddSensorValue(FileSensorValue value);
        //public void AddSensorValue(SensorValueBase value);

        //public string AddSensorValue(NewJobResult value);
        public SensorsUpdateMessage GetSensorUpdates(User user);
        public SensorsUpdateMessage GetSensorsTree(User user);
        public SensorHistoryListMessage GetSensorHistory(User user, string name, string path, string product, long n = -1);
        public ProductsListMessage GetProductsList(User user);
        public AddProductResultMessage AddProduct(User user, string productName);
        public RemoveProductResultMessage RemoveProduct(User user, string productName);
        public SignedCertificateMessage SignClientCertificate(User user, CertificateSignRequestMessage request);
        public ClientVersionMessage GetLastAvailableClientVersion();

    }
}
