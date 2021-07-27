using System;
using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.DatabaseInterface
{
    public interface IPublicAdapter : IDisposable
    {
        #region Products
        void RemoveProduct(string productName);
        void UpdateProduct(ProductEntity productEntity);
        void AddProduct(ProductEntity productEntity);
        ProductEntity GetProduct(string productName);
        List<ProductEntity> GetAllProducts();

        #endregion

        #region Sensors

        void RemoveSensor(SensorEntity sensorEntity);
        void RemoveSensor(string productName, string path);
        void AddSensor(SensorEntity sensorEntity);
        void UpdateSensor(SensorEntity sensorEntity);
        void PutSensorData(SensorDataEntity data, string productName);
        void PutOneValueSensorData(SensorDataEntity data, string productName);
        SensorDataEntity GetLastSensorValue(string productName, string path);
        SensorEntity GetSensor(string productName, string path);
        List<SensorDataEntity> GetSensorHistory(string productName, string path, long n);
        SensorDataEntity GetOneValueSensorValue(string productName, string path);
        List<SensorEntity> GetProductSensors(string productName);
        #endregion

        #region User

        void AddUser(UserEntity user);
        void UpdateUser(UserEntity user);
        void RemoveUser(UserEntity user);
        List<UserEntity> GetUsers();
        List<UserEntity> GetUsersPage(int page, int pageSize);

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