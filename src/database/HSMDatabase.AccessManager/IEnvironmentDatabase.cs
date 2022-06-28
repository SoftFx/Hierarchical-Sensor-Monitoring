using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager
{
    public interface IEnvironmentDatabase : IDisposable
    {
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
        List<string> GetSensorsStrOld();
        List<string> GetSensorsStrNew();
        void RemoveAllOldSensors();

        #endregion

        #region

        void AddPolicyIdToList(string policyId);
        void AddPolicy(PolicyEntity policy);
        List<string> GetAllPoliciesIds();
        string GetPolicy(string policyId);

        #endregion

        #region Users

        void AddUser(UserEntity user);
        List<UserEntity> ReadUsers();
        void RemoveUser(UserEntity user);
        List<UserEntity> ReadUsersPage(int page, int pageSize);

        #endregion

        #region Configuration

        ConfigurationEntity ReadConfigurationObject(string name);
        void WriteConfigurationObject(ConfigurationEntity obj);
        void RemoveConfigurationObject(string name);

        #endregion

        #region Registration Ticket

        RegisterTicketEntity ReadRegistrationTicket(Guid id);
        void RemoveRegistrationTicket(Guid id);
        void WriteRegistrationTicket(RegisterTicketEntity ticket);

        #endregion

        List<string> GetMonitoringDatabases();
        void AddMonitoringDatabaseToList(string folderName);
        void RemoveMonitoringDatabaseFromList(string folderName);
    }
}
