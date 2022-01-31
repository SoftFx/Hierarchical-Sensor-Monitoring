using HSMCommon.Constants;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Converters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Products
{
    public class ProductManager : IProductManager
    {
        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly ILogger<ProductManager> _logger;
        private readonly ConcurrentDictionary<string, Product> _products;

        public ProductManager(IDatabaseAdapter databaseAdapter, ILogger<ProductManager> logger)
        {
            _logger = logger;
            _databaseAdapter = databaseAdapter;
            _products = new ConcurrentDictionary<string, Product>();

            InitializeProducts();
        }

        public List<Product> Products
        {
            get => _products.Values.ToList();
        }

        private Product GetProduct(string name) => 
            _products.ContainsKey(name) ? _products[name] : null;
            
        private void InitializeProducts()
        {
            int count = 0;

            var existingProducts = _databaseAdapter.GetProducts();
            foreach (var product in existingProducts)
            {
                _products[product.Name] = product;

                var sensors = _databaseAdapter.GetProductSensors(product);
                product.InitializeSensors(sensors);

                ++count;
            }

            var monitoringProduct = GetProductByName(CommonConstants.SelfMonitoringProductName);
            if (count < 1 || monitoringProduct == null)
            {
                AddSelfMonitoringProduct();
            }

            _logger.LogInformation($"{_products.Count} products read, ProductManager initialized");
        }

        private void AddSelfMonitoringProduct()
        {
            Product product = new Product(CommonConstants.SelfMonitoringProductKey,
                CommonConstants.SelfMonitoringProductName, DateTime.Now);

            AddProduct(product);
        }

        public void RemoveProduct(string name)
        {
            try
            {
                _databaseAdapter.RemoveProduct(name);

                var product = GetProductByName(name);
                if (product != null)
                {
                    _products.Remove(name, out var removedProduct);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to remove product, name = {name}");
            }
        }

        public void AddProduct(string name)
        {
            string key = KeyGenerator.GenerateProductKey(name);

            _logger.LogInformation($"Created product key = '{key}' for product = '{name}'");

            Product product = new Product(key, name, DateTime.Now);
            AddProduct(product);
        }

        private void AddProduct(Product product)
        {
            try
            {
                _databaseAdapter.AddProduct(product);

                _products[product.Name] = product;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add new product, name = {product.Name}");
            }
        }

        public void UpdateProduct(Product product)
        {
            Product currentProduct = _products[product.Name];

            if (currentProduct == null)
            {
                AddProduct(product);
                return;
            }

            currentProduct.Update(product);
            _databaseAdapter.UpdateProduct(currentProduct);
        }

        public void UpdateSensorInfo(SensorInfo newInfo)
        {
            var existingInfo = GetSensorInfo(newInfo.ProductName, newInfo.Path);
            existingInfo.Update(newInfo);

            var product = GetProduct(newInfo.ProductName);
            if (product != null)
                product.AddOrUpdateSensor(newInfo);

            _databaseAdapter.UpdateSensor(existingInfo);
        }

        public bool IsSensorRegistered(string productName, string path)
        {
            var product = GetProduct(productName);
            if (product == null) return false;

            return product.Sensors.ContainsKey(path);
        }

        public void AddSensor(string productName, SensorValueBase sensorValue)
        {
            var product = GetProduct(productName);
            if (product == null) return;

            var newSensor = sensorValue.Convert(productName);

            if (!product.Sensors.ContainsKey(newSensor.Path))
            {
                product.AddOrUpdateSensor(newSensor);
                _databaseAdapter.AddSensor(newSensor);
            }
        }      

        public void RemoveSensor(string productName, string path)
        {
            var product = GetProduct(productName);
            if (product == null) return;

            try
            {
                product.RemoveSensor(path);
               _databaseAdapter.RemoveSensor(productName, path);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error while removing sensor {path} for {productName}");
            }

        }

        public string GetProductNameByKey(string key)
        {
            Product product = _products.Values.FirstOrDefault(p => p.Key.Equals(key));

            return product?.Name;
        }

        public Product GetProductByName(string name) => GetProduct(name);

        public Product GetProductByKey(string key)
        {
            Product product = _products.Values.FirstOrDefault(p => p.Key.Equals(key));

            return product;
        }

        public List<SensorInfo> GetProductSensors(string productName)
        {
            var product = GetProduct(productName);

            return product == null ? null : product.Sensors.Values.ToList();
        }

        public SensorInfo GetSensorInfo(string productName, string path)
        {
            var product = GetProduct(productName);

            if (product == null) return null;

            product.Sensors.TryGetValue(path, out var value);
            return value;
        }
    }
}
