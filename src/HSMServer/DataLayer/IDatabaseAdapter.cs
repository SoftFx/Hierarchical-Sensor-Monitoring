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
        #region Product Old

        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void RemoveProductOld(string productName);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void AddProductOld(Product product);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void UpdateProductOld(Product product);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        Product GetProductOld(string productName);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        List<Product> GetProductsOld();

        #endregion

        #region Sensors Old

        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void RemoveSensorOld(string productName, string path);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void AddSensorOld(SensorInfo info);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void UpdateSensorOld(SensorInfo info);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void PutSensorDataOld(SensorDataEntity data, string productName);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void PutOneValueSensorDataOld(SensorDataEntity data, string productName);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        SensorDataEntity GetLastSensorValueOld(string productName, string path);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        SensorInfo GetSensorInfoOld(string productName, string path);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        List<SensorHistoryData> GetSensorHistoryOld(string productName, string path, long n);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        SensorHistoryData GetOneValueSensorValueOld(string productName, string path);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        List<SensorInfo> GetProductSensorsOld(Product product);
        #endregion

        #region User Old

        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void AddUserOld(User user);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void UpdateUserOld(User user);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void RemoveUserOld(User user);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        List<User> GetUsersOld();
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        List<User> GetUsersPageOld(int page, int pageSize);

        #endregion

        #region Configuration Old
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        ConfigurationObject GetConfigurationObjectOld(string name);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void WriteConfigurationObjectOld(ConfigurationObject obj);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void RemoveConfigurationObjectOld(string name);

        [Obsolete("13.08.2021. Split databases, use methods without 'Old' postfix")]
        List<ConfigurationObject> GetAllConfigurationObjectsOld();

        #endregion

        #region Registration Ticket Old
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        RegistrationTicket ReadRegistrationTicketOld(Guid id);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void RemoveRegistrationTicketOld(Guid id);
        [Obsolete("10.08.2021. Split databases, use methods without 'Old' postfix")]
        void WriteRegistrationTicketOld(RegistrationTicket ticket);
        [Obsolete("13.08.2021. Split databases, use methods without 'Old' postfix")]
        List<RegistrationTicket> GetAllTicketsOld();

        #endregion

        #region Product

        void RemoveProduct(string productName);
        void AddProduct(Product product);
        void UpdateProduct(Product product);
        Product GetProduct(string productName);
        List<Product> GetProducts();

        #endregion

        #region Sensors

        void RemoveSensor(string productName, string path);
        void AddSensor(SensorInfo info);
        void UpdateSensor(SensorInfo info);
        void PutSensorData(SensorDataEntity data, string productName);
        SensorDataEntity GetLastSensorValue(string productName, string path);
        SensorInfo GetSensorInfo(string productName, string path);
        List<SensorHistoryData> GetAllSensorHistory(string productName, string path);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from, DateTime to);
        SensorHistoryData GetOneValueSensorValue(string productName, string path);
        List<SensorInfo> GetProductSensors(Product product);

        #endregion

        #region User

        void AddUser(User user);
        void UpdateUser(User user);
        void RemoveUser(User user);
        List<User> GetUsers();
        List<User> GetUsersPage(int page, int pageSize);

        #endregion

        #region Configuration

        ConfigurationObject GetConfigurationObject(string name);
        void WriteConfigurationObject(ConfigurationObject obj);
        void RemoveConfigurationObject(string name);

        #endregion

        #region Registration ticket

        RegistrationTicket ReadRegistrationTicket(Guid id);
        void RemoveRegistrationTicket(Guid id);
        void WriteRegistrationTicket(RegistrationTicket ticket);

        #endregion
    }
}