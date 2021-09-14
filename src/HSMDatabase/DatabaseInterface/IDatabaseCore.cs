using System;
using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.DatabaseInterface
{
    public interface IDatabaseCore
    {
        IDatabaseCore GetInstance();
        #region Sensors
        List<SensorDataEntity> GetAllSensorData(string productName, string path);
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