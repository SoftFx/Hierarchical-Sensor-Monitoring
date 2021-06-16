using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Constants;
using HSMServer.Keys;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using HSMServer.MonitoringServerCore;
using Microsoft.Extensions.Logging;
using NLog;
using Product = HSMServer.DataLayer.Model.Product;

namespace HSMServer.Products
{
    public class ProductManager : IProductManager
    {
        private readonly IDatabaseClass _database;
        private readonly ILogger<ProductManager> _logger;
        private readonly List<Product> _products;
        private readonly Dictionary<string, List<SensorInfo>> _productSensorsDictionary = new Dictionary<string, List<SensorInfo>>();
        private readonly object _productsLock = new object();
        private readonly object _dictionaryLock = new object();
        public ProductManager(IDatabaseClass database, ILogger<ProductManager> logger)
        {
            _logger = logger;
            _database = database;
            _products = new List<Product>();
            InitializeProducts();
        }

        public List<Product> Products
        {
            get
            {
                lock (_productsLock)
                {
                    return _products.ToList();
                }
            }
        }

        //TODO: read products via product info, like users are read now
        private void InitializeProducts()
        {
            List<string> productNames = _database.GetProductsList();
            foreach (var productName in productNames)
            {
                var info = _database.GetProductInfo(productName);
                var sensorPaths = _database.GetSensorsList(productName);
                List<SensorInfo> sensorInfos = new List<SensorInfo>();
                foreach (var sensorPath in sensorPaths)
                {
                    var existingInfo = _database.GetSensorInfo(productName, sensorPath);
                    if (existingInfo == null)
                    {
                        SensorInfo defaultInfo = Converter.Convert(productName, sensorPath);
                        sensorInfos.Add(defaultInfo);
                        Task.Run(() => _database.AddSensor(defaultInfo));
                        sensorInfos.Add(defaultInfo);
                    }
                    else
                    {
                        sensorInfos.Add(existingInfo);
                    }
                }
                if (info != null)
                {
                    lock (_productsLock)
                    {
                        _products.Add(info);
                    }

                    lock (_dictionaryLock)
                    {
                        _productSensorsDictionary[productName] = new List<SensorInfo>();
                        _productSensorsDictionary[productName].AddRange(sensorInfos);
                    }
                }
            }

            if (productNames.Count < 1)
            {
                AddDefaultProduct();
            }

            lock (_productsLock)
            {
                _logger.LogInformation($"{_products.Count} products read, ProductManager initialized");
            }
            
        }

        private void AddDefaultProduct()
        {
            Product product = new Product();
            product.Name = TextConstants.DefaultProductName;
            product.Key = TextConstants.DefaultProductKey;
            product.DateAdded = DateTime.Now;
            AddProduct(product);
        }

        public void RemoveProduct(string name)
        {
            try
            {
                _database.RemoveProductFromList(name);
                _database.RemoveProductInfo(name);
                var product = GetProductByName(name);
                if (product != null)
                {
                    lock (_productsLock)
                    {
                        _products.Remove(product);
                    }

                    lock (_dictionaryLock)
                    {
                        _productSensorsDictionary[product.Name]?.Clear();
                        _productSensorsDictionary.Remove(product.Name);
                    }
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
            Product product = new Product {Key = key, Name = name, DateAdded = DateTime.Now};
            AddProduct(product);
        }

        private void AddProduct(Product product)
        {
            try
            {
                _database.AddProductToList(product.Name);
                _database.PutProductInfo(product);
                lock (_productsLock)
                {
                    _products.Add(product);
                }

                lock (_dictionaryLock)
                {
                    _productSensorsDictionary[product.Name] = new List<SensorInfo>();
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add new product, name = {product.Name}");
            }
        }
        public bool IsSensorRegistered(string productName, string path)
        {
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(productName))
                    return false;

                return _productSensorsDictionary[productName].FirstOrDefault(s => s.Path == path) != null;
            }
        }

        public void AddSensor(string productName, SensorValueBase sensorValue)
        {
            var newObject = Converter.Convert(productName, sensorValue);
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(productName))
                {
                    _productSensorsDictionary[productName] = new List<SensorInfo>();
                }
                _productSensorsDictionary[productName].Add(newObject);
            }

            Task.Run(() => _database.AddNewSensorToList(productName, newObject.Path));
            Task.Run(() => _database.AddSensor(newObject));
        }

        public void AddSensorIfNotRegistered(string productName, SensorValueBase sensorValue)
        {
            bool needToAdd = false;
            var newObject = Converter.Convert(productName, sensorValue);
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(productName))
                {
                    _productSensorsDictionary[productName] = new List<SensorInfo>();
                    needToAdd = true;
                }

                var existingSensor = _productSensorsDictionary[productName]
                    .FirstOrDefault(s => s.Path == sensorValue.Path);

                if (existingSensor == null || !string.IsNullOrEmpty(sensorValue.Description))
                {
                    _productSensorsDictionary[productName].Add(newObject);
                    needToAdd = true;
                }
            }

            if (needToAdd)
            {
                Task.Run(() => _database.AddNewSensorToList(productName, newObject.Path));
                Task.Run(() => _database.AddSensor(newObject));
            }
        }

        public void AddSensor(SensorInfo sensorInfo)
        {
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(sensorInfo.ProductName))
                {
                    _productSensorsDictionary[sensorInfo.ProductName] = new List<SensorInfo>();
                }
                _productSensorsDictionary[sensorInfo.ProductName].Add(sensorInfo);
            }

            //ThreadPool.QueueUserWorkItem(_ => _database.AddSensor(sensorInfo));
            //ThreadPool.QueueUserWorkItem(_ =>
            //    _database.AddNewSensorToList(sensorInfo.ProductName, sensorInfo.Path));
            Task.Run(() => _database.AddNewSensorToList(sensorInfo.ProductName, sensorInfo.Path));
            Task.Run(() => _database.AddSensor(sensorInfo));
        }
        public string GetProductKeyByName(string name)
        {
            Product product = null;
            lock (_productsLock)
            {
                product = _products.FirstOrDefault(p => p.Name.Equals(name));
            }
            return product?.Key;
        }

        public string GetProductNameByKey(string key)
        {
            Product product = null;
            lock (_productsLock)
            {
                product = _products.FirstOrDefault(p => p.Key.Equals(key));
            }
            return product?.Name;
        }

        public Product GetProductByName(string name)
        {
            Product product = null;
            lock (_productsLock)
            {
                product = _products.FirstOrDefault(p => p.Name.Equals(name));
            }

            return product;
        }

        public List<SensorInfo> GetProductSensors(string productName)
        {
            List<SensorInfo> result = new List<SensorInfo>();
            lock (_dictionaryLock)
            {
                var pair = _productSensorsDictionary.FirstOrDefault(p => p.Key == productName);
                result.AddRange(pair.Value);
            }

            return result;
        }
    }
}
