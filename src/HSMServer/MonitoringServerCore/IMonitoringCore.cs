using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Authentication;
using HSMServer.DataLayer.Model;
using HSMServer.Model.SensorsData;
using HSMService;

namespace HSMServer.MonitoringServerCore
{
    public interface IMonitoringCore : IDisposable
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
        //public SensorsUpdateMessage GetSensorUpdates(User user);
        public List<SensorData> GetSensorUpdates(User user);
        //public SensorsUpdateMessage GetSensorsTree(User user);
        public List<SensorData> GetSensorsTree(User user);
        //public SensorHistoryListMessage GetSensorHistory(User user, string name, string path, string product, long n = -1);
        public List<SensorHistoryData> GetSensorHistory(User user, string name, string path, string product,
            long n = -1);
        public string GetFileSensorValue(User user, string product, string path);
        //public StringMessage GetFileSensorValueExtension(User user, string product, string path);
        public string GetFileSensorValueExtension(User user, string product, string path);
        //public ProductsListMessage GetProductsList(User user);
        public List<Product> GetProductsList(User user);
        //public AddProductResultMessage AddProduct(User user, string productName);
        public bool AddProduct(User user, string productName, out Product product, out string error);
        //public RemoveProductResultMessage RemoveProduct(User user, string productName);
        public bool RemoveProduct(User user, string productName, out Product product, out string error);
        public SignedCertificateMessage SignClientCertificate(User user, CertificateSignRequestMessage request);
        //public ClientVersionMessage GetLastAvailableClientVersion();
        public string GetLastAvailableClientVersion();
    }
}
