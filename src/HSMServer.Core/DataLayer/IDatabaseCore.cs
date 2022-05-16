﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.DataLayer
{
    public interface IDatabaseCore : IDisposable
    {
        #region Size

        long GetDatabaseSize();
        long GetMonitoringDataSize();
        long GetEnvironmentDatabaseSize();

        #endregion

        #region Product

        void RemoveProduct(string productName);
        void RemoveProductNew(string id);
        void AddProduct(Product product);
        void AddProduct(ProductEntity entity);
        void AddProductNew(Product product);
        void AddProductNew(ProductEntity entity);
        void UpdateProduct(ProductEntity entity);
        Product GetProductNew(string id);
        List<Product> GetProducts();
        List<ProductEntity> GetAllProducts();

        #endregion

        #region AccessKey

        void RemoveAccessKey(string id);
        void AddAccessKey(AccessKeyEntity entity);
        void UpdateAccessKey(AccessKeyEntity entity);
        AccessKeyEntity GetAccessKey(string id);
        List<AccessKeyEntity> GetAccessKeys();

        #endregion

        #region Sensors

        void RemoveSensor(string productName, string path);
        void RemoveSensorWithMetadata(string productName, string path);
        void AddSensor(SensorInfo info);
        void AddSensor(SensorEntity entity);
        void UpdateSensor(SensorInfo info);
        void UpdateSensor(SensorEntity entity);
        void PutSensorData(SensorDataEntity data, string productName);
        SensorDataEntity GetLatestSensorValue(string productName, string path);
        SensorInfo GetSensorInfo(string productName, string path);
        List<SensorHistoryData> GetAllSensorHistory(string productName, string path);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, DateTime from, DateTime to);
        List<SensorHistoryData> GetSensorHistory(string productName, string path, int n);
        SensorHistoryData GetOneValueSensorValue(string productName, string path);
        List<SensorInfo> GetProductSensors(string  productName);

        List<SensorEntity> GetAllSensors();
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