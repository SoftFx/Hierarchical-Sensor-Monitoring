using System;
using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.DatabaseInterface
{
    public interface IDatabaseCore
    {
        #region Sensors

        List<SensorDataEntity> GetAllSensorData(string productName, string path);
        List<SensorDataEntity> GetSensorData(string productName, string path, DateTime from);
        List<SensorDataEntity> GetSensorData(string productName, string path, DateTime from, DateTime to);
        long GetSensorSize(string productName, string path);
        void AddSensorValue(SensorDataEntity entity, string productName);
        SensorDataEntity GetLatestSensorValue(string productName, string path);
        void RemoveSensor(string productName, string path);

        #endregion

        void RemoveProduct(string productName);
        void UpdateProduct(ProductEntity productEntity);
        void AddProduct(ProductEntity productEntity);
        ProductEntity GetProduct(string productName);
        List<ProductEntity> GetAllProducts();
    }
}