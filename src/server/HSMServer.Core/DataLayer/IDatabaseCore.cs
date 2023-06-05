using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model;
using HSMServer.Core.Registration;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.DataLayer
{
    public interface IDatabaseCore : IDisposable
    {
        ISnapshotDatabase Snapshots { get; }


        long TotalDbSize { get; }

        long SensorHistoryDbSize { get; }

        long EnviromentDbSize { get; }


        #region Folders

        public void AddFolder(FolderEntity entity);
        public void UpdateFolder(FolderEntity entity);
        public void RemoveFolder(string id);
        public FolderEntity GetFolder(string id);
        public List<FolderEntity> GetAllFolders();

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
        void ClearSensorValues(string sensorId, DateTime from, DateTime to);


        Dictionary<Guid, byte[]> GetLatestValues(Dictionary<Guid, long> sensors);

        Dictionary<Guid, byte[]> GetLatestValuesFrom(Dictionary<Guid, long> sensors);

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