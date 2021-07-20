using System;
using System.Collections.Generic;
using HSMCommon.Model.SensorsData;
using HSMDatabase.Entity;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.DataLayer.Model;
using HSMServer.Registration;

namespace HSMServer.DataLayer
{
    public interface IDatabaseAdapter
    {
        #region Product

        void RemoveProduct(string productName);
        void AddProduct(Product product);
        Product GetProduct(string productName);
        List<Product> GetProducts();

        #endregion

        #region Sensors

        void RemoveSensor(string productName, string sensorName);
        void AddSensor(SensorInfo info);
        void PutSensorData(SensorDataEntity data, string productName);
        void PutOneValueSensorData(SensorDataEntity data, string productName);
        SensorDataEntity GetLastSensorValue(string productName, string path);
        SensorInfo GetSensorInfo(string productName, string path);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, long n);
        SensorHistoryData GetOneValueSensorValue(string productName, string path);
        #endregion

        #region User

        void AddUser(User user);
        void RemoveUser(User user);
        List<User> GetUsers();
        List<User> GetUsersPage(int page, int pageSize);

        #endregion

        #region Configuration

        ConfigurationObject GetConfigurationObject(string name);
        void WriteConfigurationObject(ConfigurationObject obj);

        #endregion

        #region Registration Ticket

        RegistrationTicket ReadRegistrationTicket(Guid id);
        void RemoveRegistrationTicket(Guid id);
        void WriteRegistrationTicket(RegistrationTicket ticket);

        #endregion
    }
}