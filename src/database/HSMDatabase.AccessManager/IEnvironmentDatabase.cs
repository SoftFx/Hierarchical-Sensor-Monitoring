using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager
{
    public interface IEnvironmentDatabase : IDisposable
    {
        #region Folders

        void PutFolder(FolderEntity entity);
        void RemoveFolder(string id);
        void AddFolderToList(string id);
        void RemoveFolderFromList(string id);
        FolderEntity GetFolder(string id);
        List<string> GetFoldersList();

        #endregion

        #region Products

        void AddProductToList(string productName);
        List<string> GetProductsList();
        ProductEntity GetProduct(string id);
        void PutProduct(ProductEntity product);
        void RemoveProduct(string id);
        void RemoveProductFromList(string productName);

        #endregion

        #region AccessKey

        void AddAccessKeyToList(string id);
        List<string> GetAccessKeyList();
        void RemoveAccessKeyFromList(string id);
        void AddAccessKey(AccessKeyEntity entity);
        void RemoveAccessKey(string id);
        AccessKeyEntity GetAccessKey(string id);

        #endregion

        #region Sensors

        void AddSensorIdToList(string sensorId);
        void AddSensor(SensorEntity info);
        void RemoveSensorIdFromList(string sensorId);
        void RemoveSensor(string sensorId);
        SensorEntity GetSensorEntity(string sensorId);
        List<string> GetAllSensorsIds();

        #endregion

        #region Policy

        [Obsolete("Will be removed after policy migration")]
        List<string> GetAllOldPoliciesIds();
        [Obsolete("Will be removed after policy migration")]
        byte[] GetOldPolicy(string policyId);
        [Obsolete("Will be removed after policy migration")]
        void RemoveOldPolicy(string policyId);
        [Obsolete("Will be removed after policy migration")]
        void DropOldPolicyIdsList();

        List<byte[]> GetAllPoliciesIds();
        PolicyEntity GetPolicy(byte[] policyId);
        void AddPolicyIdToList(Guid policyId);
        void AddPolicy(PolicyEntity policy);
        void RemovePolicy(Guid policyId);
        #endregion

        #region Users

        void AddUser(UserEntity user);
        List<UserEntity> ReadUsers();
        void RemoveUser(UserEntity user);
        List<UserEntity> ReadUsersPage(int page, int pageSize);

        #endregion
    }
}
