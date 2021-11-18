using System;
using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.DatabaseInterfaces
{
    public interface IDatabaseCore
    {
        /// <summary>
        /// Get size of the whole database
        /// </summary>
        /// <returns>Databases folder size in bytes, including environment and all monitoring databases</returns>
        long GetDatabaseSize();

        /// <summary>
        /// Get the size of all monitoring data
        /// </summary>
        /// <returns>Size of all monitoring databases in bytes</returns>
        long GetMonitoringDataSize();

        /// <summary>
        /// Get size of environment database
        /// </summary>
        /// <returns>Size of environment database in bytes</returns>
        long GetEnvironmentDatabaseSize();

        #region Sensors

        List<SensorDataEntity> GetAllSensorData(string productName, string path);
        List<SensorDataEntity> GetSensorData(string productName, string path, int n);
        List<SensorDataEntity> GetSensorData(string productName, string path, DateTime from);
        List<SensorDataEntity> GetSensorData(string productName, string path, DateTime from, DateTime to);
        void AddSensorValue(SensorDataEntity entity, string productName);
        SensorDataEntity GetLatestSensorValue(string productName, string path);
        void RemoveSensor(string productName, string path);
        void AddSensor(SensorEntity entity);
        SensorEntity GetSensorInfo(string productName, string path);
        List<SensorEntity> GetProductSensors(string productName);
        long GetSensorSize(string productName, string path);

        #endregion

        #region Products

        void RemoveProduct(string productName);
        void UpdateProduct(ProductEntity productEntity);
        void AddProduct(ProductEntity productEntity);
        ProductEntity GetProduct(string productName);
        List<ProductEntity> GetAllProducts();

        #endregion

        #region User

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
    }
}