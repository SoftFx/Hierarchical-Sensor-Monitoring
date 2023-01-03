using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.DataLayer
{
    public interface IDatabaseCore : IDisposable
    {
        #region Size

        long GetDatabaseSize();
        long GetSensorsHistoryDatabaseSize();
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

        void AddSensor(SensorEntity entity);
        void UpdateSensor(SensorEntity entity);
        void RemoveSensorWithMetadata(string sensorId);

        void AddSensorValue(SensorValueEntity valueEntity);
        void ClearSensorValues(string sensorId);

        Dictionary<Guid, byte[]> GetLatestValues(List<BaseSensorModel> sensors);
        IAsyncEnumerable<List<byte[]>> GetSensorValuesPage(string sensorId, DateTime from, DateTime to, int count);

        List<SensorEntity> GetAllSensors();

        #endregion

        #region Policies

        void AddPolicy(PolicyEntity policy);
        void UpdatePolicy(PolicyEntity policy);
        void RemovePolicy(Guid id);
        List<byte[]> GetAllPolicies();

        #endregion

        #region User

        void AddUser(UserEntity user);
        void UpdateUser(UserEntity user);
        void RemoveUser(UserEntity user);
        List<UserEntity> GetUsers();
        List<UserEntity> GetUsersPage(int page, int pageSize);

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