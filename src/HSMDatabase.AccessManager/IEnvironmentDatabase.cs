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

        #region Sensors

        void RemoveSensor(string productName, string path);
        void AddSensor(SensorEntity info);
        SensorEntity GetSensorInfo(string productName, string path);
        List<SensorEntity> GetSensorsInfo();

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
