using System.Collections.Generic;
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
        List<string> GetProductSensors(string productName);
        bool IsSensorRegistered(string productName, string path);
        void AddSensor(string productName, string path);
        void AddSensorIfNotRegistered(string productName, string path);
    }
}