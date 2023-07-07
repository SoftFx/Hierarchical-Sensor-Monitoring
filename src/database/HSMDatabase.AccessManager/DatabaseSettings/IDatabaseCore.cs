using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
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
        List<PolicyEntity> GetAllPolicies();
        void AddPolicy(PolicyEntity policy);
        void UpdatePolicy(PolicyEntity policy);
        void RemovePolicy(Guid id);

        [Obsolete("Will be removed after policy migration")]
        List<byte[]> GetAllOldPolicies();
        [Obsolete("Will be removed after policy migration")]
        void RemoveAllOldPolicies();

        #endregion

        #region User

        void AddUser(UserEntity user);
        void UpdateUser(UserEntity user);
        void RemoveUser(UserEntity user);
        List<UserEntity> GetUsers();
        List<UserEntity> GetUsersPage(int page, int pageSize);

        #endregion

        #region Journal

        void AddJournalValue(JournalKey journalKey, JournalEntity value);
        void RemoveJournalValue(Guid id);
        IAsyncEnumerable<List<(byte[] Key, byte[] Entity)>> GetJournalValuesPage(Guid sensorId, DateTime from, DateTime to, RecordType recordType, int count);

        #endregion
    }
}