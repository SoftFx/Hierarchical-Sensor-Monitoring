using System.Collections.Generic;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.Products
{
    public interface IProductManager
    {
        List<Product> Products { get; }
        void RemoveProduct(string name);
        void AddProduct(string name);
        string GetProductNameByKey(string key);
        Product GetProductByName(string name);
        Product GetProductByKey(string key);
        List<SensorInfo> GetProductSensors(string productName);

        SensorInfo GetSensorInfo(string productName, string path);
        void UpdateSensorInfo(SensorInfo newInfo);
        bool IsSensorRegistered(string productName, string path);
        void AddSensor(string productName, SensorValueBase sensorValue);
        void RemoveSensor(string productName, string path);
        void UpdateProduct(Product product);
    }
}