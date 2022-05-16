using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.AccessManager
{
    public interface IEnvironmentDatabase : IDisposable
    {
        #region Products

        void AddProductToList(string productName);
        List<string> GetProductsList();
        ProductEntity GetProductInfo(string productName);
        ProductEntity GetProductInfoNew(string id);
        string GetProductInfoStr(string productName);
        string GetProductInfoStrNew(string id);
        void PutProductInfo(ProductEntity product);
        void PutProductInfoNew(ProductEntity product);
        void RemoveProductInfo(string productName);
        void RemoveProductInfoNew(string id);
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

        void RemoveSensor(string productName, string path);
        void AddSensor(SensorEntity info);
        List<string> GetSensorsList(string productName);
        void AddNewSensorToList(string productName, string path);
        void RemoveSensorsList(string productName);
        void RemoveSensorFromList(string productName, string path);
        SensorEntity GetSensorInfo(string productName, string path);
        List<SensorEntity> GetSensorsInfo();
        void RemoveSensorValues(string productName, string path);

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
