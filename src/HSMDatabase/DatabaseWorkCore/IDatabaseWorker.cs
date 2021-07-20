using System;
using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.DatabaseWorkCore
{
    internal interface IDatabaseWorker : IDisposable
    {
        #region Management

        void CloseDatabase();
        void OpenDatabase(string databaseName);
        void DeleteDatabase();

        #endregion

        #region Products

        void AddProductToList(string productName);
        List<string> GetProductsList();
        ProductEntity GetProductInfo(string productName);
        void PutProductInfo(ProductEntity product);
        void RemoveProductInfo(string name);
        void RemoveProductFromList(string name);

        #endregion

        #region Sensors

        void RemoveSensor(string productName, string path);
        void AddSensor(SensorEntity info);
        void WriteSensorData(SensorDataEntity dataObject, string productName);
        /// <summary>
        /// Use for sensors, for which only last value must be stored
        /// </summary>
        /// <param name="dataObject"></param>
        /// <param name="productName"></param>
        void WriteOneValueSensorData(SensorDataEntity dataObject, string productName);
        SensorDataEntity GetOneValueSensorValue(string productName, string path);
        SensorDataEntity GetLastSensorValue(string productName, string path);
        List<SensorDataEntity> GetSensorDataHistory(string productName, string path, long n);
        List<string> GetSensorsList(string productName);
        void AddNewSensorToList(string productName, string path);
        void RemoveSensorsList(string productName);
        void RemoveSensorFromList(string productName, string sensorName);
        SensorEntity GetSensorInfo(string productName, string path);
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

        #endregion

        #region Registration Ticket

        RegisterTicketEntity ReadRegistrationTicket(Guid id);
        void RemoveRegistrationTicket(Guid id);
        void WriteRegistrationTicket(RegisterTicketEntity ticket);

        #endregion
    }
}