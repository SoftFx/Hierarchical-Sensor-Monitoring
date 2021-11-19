using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.AccessManager
{
    public interface IEnvironmentDatabase
    {
        #region Products

        void AddProductToList(string productName);
        List<string> GetProductsList();
        IProductEntity GetProductInfo(string productName);
        void PutProductInfo(IProductEntity product);
        void RemoveProductInfo(string productName);
        void RemoveProductFromList(string productName);

        #endregion

        #region Sensors

        void RemoveSensor(string productName, string path);
        void AddSensor(ISensorEntity info);
        List<string> GetSensorsList(string productName);
        void AddNewSensorToList(string productName, string path);
        void RemoveSensorsList(string productName);
        void RemoveSensorFromList(string productName, string path);
        ISensorEntity GetSensorInfo(string productName, string path);
        void RemoveSensorValues(string productName, string path);

        #endregion

        #region Users

        void AddUser(IUserEntity user);
        List<IUserEntity> ReadUsers();
        void RemoveUser(IUserEntity user);
        List<IUserEntity> ReadUsersPage(int page, int pageSize);

        #endregion

        #region Configuration

        IConfigurationEntity ReadConfigurationObject(string name);
        void WriteConfigurationObject(IConfigurationEntity obj);
        void RemoveConfigurationObject(string name);

        #endregion

        #region Registration Ticket

        IRegisterTicketEntity ReadRegistrationTicket(Guid id);
        void RemoveRegistrationTicket(Guid id);
        void WriteRegistrationTicket(IRegisterTicketEntity ticket);

        #endregion

        List<string> GetMonitoringDatabases();
        void AddMonitoringDatabaseToList(string folderName);
        void RemoveMonitoringDatabaseFromList(string folderName);
    }
}
