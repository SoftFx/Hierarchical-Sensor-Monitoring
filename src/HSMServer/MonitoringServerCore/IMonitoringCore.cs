using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Authentication;
using HSMServer.DataLayer.Model;
using RSAParameters = System.Security.Cryptography.RSAParameters;

namespace HSMServer.MonitoringServerCore
{
    public interface IMonitoringCore : IDisposable
    {
        //public void AddSensorValue(JobResult value);
        void AddSensorsValues(IEnumerable<CommonSensorValue> values);
        Task<bool> AddSensorValueAsync(BoolSensorValue value);
        void AddSensorValue(BoolSensorValue value);
        void AddSensorValue(IntSensorValue value);
        void AddSensorValue(DoubleSensorValue value);
        void AddSensorValue(StringSensorValue value);
        void AddSensorValue(IntBarSensorValue value);
        void AddSensorValue(DoubleBarSensorValue value);

        void AddSensorValue(FileSensorValue value);
        //public void AddSensorValue(SensorValueBase value);

        //public string AddSensorValue(NewJobResult value);
        //public SensorsUpdateMessage GetSensorUpdates(User user);
        List<SensorData> GetSensorUpdates(User user);
        //public SensorsUpdateMessage GetSensorsTree(User user);
        List<SensorData> GetSensorsTree(User user);
        //public SensorHistoryListMessage GetSensorHistory(User user, string name, string path, string product, long n = -1);
        List<SensorHistoryData> GetSensorHistory(User user, string path, string product, long n = -1);
        List<SensorHistoryData> GetSensorHistory(User user, GetSensorHistoryModel model);
        string GetFileSensorValue(User user, string product, string path);
        //public StringMessage GetFileSensorValueExtension(User user, string product, string path);
        string GetFileSensorValueExtension(User user, string product, string path);
        //public ProductsListMessage GetProductsList(User user);
        List<Product> GetProductsList(User user);
        //public AddProductResultMessage AddProduct(User user, string productName);
        bool AddProduct(User user, string productName, out Product product, out string error);
        //public RemoveProductResultMessage RemoveProduct(User user, string productName);
        bool RemoveProduct(User user, string productName, out Product product, out string error);
        (X509Certificate2, X509Certificate2) SignClientCertificate(User user, string subject, string commonName,
            RSAParameters rsaParameters);
        //public ClientVersionMessage GetLastAvailableClientVersion();
        ClientVersionModel GetLastAvailableClientVersion();
    }
}
