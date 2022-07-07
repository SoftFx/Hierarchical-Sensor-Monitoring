using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.DataLayer
{
    public interface IDatabaseCore : IDisposable
    {
        #region Size

        long GetDatabaseSize();
        long GetMonitoringDataSize();
        long GetEnvironmentDatabaseSize();

        #endregion

        #region Product

        void AddProduct(ProductEntity entity);
        void UpdateProduct(ProductEntity entity);
        void RemoveProduct(string id);
        ProductEntity GetProduct(string id);
        List<ProductEntity> GetAllProducts();

        #endregion

        #region AccessKey

        void RemoveAccessKey(Guid id);
        void AddAccessKey(AccessKeyEntity entity);
        void UpdateAccessKey(AccessKeyEntity entity);
        AccessKeyEntity GetAccessKey(Guid id);
        List<AccessKeyEntity> GetAccessKeys();

        #endregion

        #region Sensors

        void RemoveSensor(string productName, string path);
        void RemoveSensorWithMetadata(string sensorId, string productName, string path);
        void AddSensor(SensorEntity entity);
        void UpdateSensor(SensorEntity entity);
        void PutSensorData(SensorDataEntity data, string productName);
        SensorDataEntity GetLatestSensorValue(string productName, string path);
        List<SensorHistoryData> GetAllSensorHistory(string productName, string path);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from, DateTime to);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, int n);
        SensorHistoryData GetOneValueSensorValue(string productName, string path);

        Dictionary<byte[], (Guid sensorId, byte[] latestValue)> GetLatestValues(List<BaseSensorModel> sensors);

        List<SensorEntity> GetAllSensors();
        void RemoveAllOldSensors();

        #endregion

        #region Policies

        void AddPolicy(PolicyEntity policy);
        List<byte[]> GetAllPolicies();

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