using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.DataLayer
{
    public interface IDatabaseAdapter : IDisposable
    {
        #region Size

        long GetDatabaseSize();
        long GetMonitoringDataSize();
        long GetEnvironmentDatabaseSize();

        #endregion

        #region Product

        void RemoveProduct(string productName);
        void AddProduct(Product product);
        void AddProduct(ProductEntity product);
        void UpdateProduct(Product product);
        void UpdateProduct(ProductEntity product);
        Product GetProduct(string productName);
        List<Product> GetProducts();
        List<ProductEntity> GetAllProducts();

        #endregion

        #region Sensors

        void RemoveSensor(string productName, string path);
        void RemoveSensorWithMetadata(string productName, string path);
        void AddSensor(SensorInfo info);
        void UpdateSensor(SensorInfo info);
        void UpdateSensor(SensorEntity sensor);
        void PutSensorData(SensorDataEntity data, string productName);
        SensorDataEntity GetLastSensorValue(string productName, string path);
        SensorInfo GetSensorInfo(string productName, string path);
        List<SensorHistoryData> GetAllSensorHistory(string productName, string path);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from, DateTime to);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, int n);
        SensorHistoryData GetOneValueSensorValue(string productName, string path);
        List<SensorInfo> GetProductSensors(Product product);

        List<SensorEntity> GetAllSensors();

        #endregion

        #region User

        void AddUser(User user);
        void UpdateUser(User user);
        void RemoveUser(User user);
        List<User> GetUsers();
        List<User> GetUsersPage(int page, int pageSize);

        #endregion

        #region Configuration

        ConfigurationObject GetConfigurationObject(string name);
        void WriteConfigurationObject(ConfigurationObject obj);
        void RemoveConfigurationObject(string name);

        #endregion

        #region Registration ticket

        RegistrationTicket ReadRegistrationTicket(Guid id);
        void RemoveRegistrationTicket(Guid id);
        void WriteRegistrationTicket(RegistrationTicket ticket);

        #endregion
    }
}