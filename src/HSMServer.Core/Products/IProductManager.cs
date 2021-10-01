using System.Collections.Generic;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;

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
        List<SensorInfo> GetAllExistingSensorInfos();
        bool IsSensorRegistered(string productName, string path);
        void AddSensor(string productName, SensorValueBase sensorValue);
        void AddSensorIfNotRegistered(string productName, SensorValueBase sensorValue);
        void RemoveSensor(string productName, string path);
        void UpdateProduct(Product product);
    }
}