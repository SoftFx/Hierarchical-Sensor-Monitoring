using System.Collections.Generic;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.DataLayer.Model;

namespace HSMServer.Products
{
    public interface IProductManager
    {
        List<Product> Products { get; }
        void RemoveProduct(string name);
        void AddProduct(string name);
        string GetProductNameByKey(string key);
        Product GetProductByName(string name);
        List<SensorInfo> GetProductSensors(string productName);
        bool IsSensorRegistered(string productName, string path);
        void AddSensor(string productName, SensorValueBase sensorValue);
        void AddSensorIfNotRegistered(string productName, SensorValueBase sensorValue);
        void RemoveSensor(string productName, string path);
        void UpdateProduct(Product product);
    }
}