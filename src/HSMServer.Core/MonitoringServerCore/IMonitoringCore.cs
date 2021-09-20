using HSMCommon.Model;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using RSAParameters = System.Security.Cryptography.RSAParameters;

namespace HSMServer.Core.MonitoringServerCore
{
    public interface IMonitoringCore : IDisposable


    {
        //public void AddSensorValue(JobResult value);
        [Obsolete("08.07.2021. Use void AddSensorsValues(IEnumerable<CommonSensorValue> values)")]
        void AddSensorsValues(List<CommonSensorValue> values);
        void AddSensorsValues(List<UnitedSensorValue> values);
        //Task<bool> AddSensorValueAsync(BoolSensorValue value);
        void AddSensorValue(BoolSensorValue value);
        void AddSensorValue(IntSensorValue value);
        void AddSensorValue(DoubleSensorValue value);
        void AddSensorValue(StringSensorValue value);
        void AddSensorValue(IntBarSensorValue value);
        void AddSensorValue(DoubleBarSensorValue value);
        void AddSensorValue(FileSensorValue value);
        void AddSensorValue(FileSensorBytesValue value);
        //public void AddSensorValue(SensorValueBase value);

        //public string AddSensorValue(NewJobResult value);
        //public SensorsUpdateMessage GetSensorUpdates(User user);
        List<SensorData> GetSensorUpdates(User user);
        //public SensorsUpdateMessage GetSensorsTree(User user);
        List<SensorData> GetSensorsTree(User user);
        //public SensorHistoryListMessage GetSensorHistory(User user, string name, string path, string product, long n = -1);
        [Obsolete("16.09.2021. Use GetSensorHistory(User user, GetSensorHistoryModel model) or pass product, path and dates")]
        List<SensorHistoryData> GetSensorHistory(User user, string path, string product, long n = -1);
        List<SensorHistoryData> GetSensorHistory(User user, string product, string path, DateTime from, DateTime to);
        List<SensorHistoryData> GetAllSensorHistory(User user, string product, string path);
        string GetFileSensorValue(User user, string product, string path);
        byte[] GetFileSensorValueBytes(User user, string product, string path);
        //public StringMessage GetFileSensorValueExtension(User user, string product, string path);
        string GetFileSensorValueExtension(User user, string product, string path);
        //public ProductsListMessage GetProductsList(User user);
        public Product GetProduct(string productKey);
        List<Product> GetProducts(User user);
        List<Product> GetAllProducts();
        //public AddProductResultMessage AddProduct(User user, string productName);
        bool AddProduct(User user, string productName, out Product product, out string error);
        //public RemoveProductResultMessage RemoveProduct(User user, string productName);
        bool RemoveProduct(string productKey, out string error);
        bool RemoveProduct(User user, string productName, out Product product, out string error);
        void UpdateProduct(User user, Product product);
        (X509Certificate2, X509Certificate2) SignClientCertificate(User user, string subject, string commonName,
            RSAParameters rsaParameters);
        //public ClientVersionMessage GetLastAvailableClientVersion();
        ClientVersionModel GetLastAvailableClientVersion();
    }
}
