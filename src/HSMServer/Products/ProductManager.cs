using HSMSensorDataObjects.FullDataObject;
using HSMServer.Constants;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using HSMServer.Keys;
using HSMServer.MonitoringServerCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Product = HSMServer.DataLayer.Model.Product;

namespace HSMServer.Products
{
    public class ProductManager : IProductManager
    {
        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly ILogger<ProductManager> _logger;
        private readonly IConverter _converter;
        private readonly List<Product> _products;
        private readonly Dictionary<string, List<SensorInfo>> _productSensorsDictionary = new Dictionary<string, List<SensorInfo>>();
        private readonly object _productsLock = new object();
        private readonly object _dictionaryLock = new object();
        public ProductManager(IDatabaseAdapter databaseAdapter, IConverter converter, ILogger<ProductManager> logger)
        {
            _logger = logger;
            _databaseAdapter = databaseAdapter;
            _converter = converter;
            _products = new List<Product>();
            MigrateProductsToNewDatabase();
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
        /// <summary>
        /// This method MUST be called when update from version 2.1.4 or lower to versions 2.1.5 or higher
        /// </summary>
        private void MigrateProductsToNewDatabase()
        {
            var existingProducts = _databaseAdapter.GetProductsOld();
            foreach (var product in existingProducts)
            {
                var sensors = _databaseAdapter.GetProductSensorsOld(product);
                _databaseAdapter.AddProduct(product);
                foreach (var sensor in sensors)
                {
                    _databaseAdapter.AddSensor(sensor);
                }
            }
        }
        //TODO: read products via product info, like users are read now
        private void InitializeProducts()
        {
            int count = 0;
            var existingProducts = _databaseAdapter.GetProductsOld();
            foreach (var product in existingProducts)
            {
                lock (_productsLock)
                {
                    _products.Add(product);
                }
                var sensors = _databaseAdapter.GetProductSensorsOld(product);
                lock (_dictionaryLock)
                {
                    _productSensorsDictionary[product.Name] = new List<SensorInfo>();
                    _productSensorsDictionary[product.Name].AddRange(sensors);
                }

                ++count;
            }

            if (count < 1)
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
            Product product = new Product(TextConstants.DefaultProductName, TextConstants.DefaultProductName, DateTime.Now);
            AddProduct(product);
        }

        public void RemoveProduct(string name)
        {
            try
            {
                _databaseAdapter.RemoveProductOld(name);
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
            Product product = new Product(key, name, DateTime.Now);
            AddProduct(product);
        }

        private void AddProduct(Product product)
        {
            try
            {
                _databaseAdapter.AddProductOld(product);
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

        public void UpdateProduct(Product product)
        {
            Product currentProduct;
            lock (_productsLock)
            {
                currentProduct = _products.FirstOrDefault(p => p.Key.Equals(product.Key)
                && p.Name.Equals(product.Name, StringComparison.InvariantCultureIgnoreCase));
            }

            if (currentProduct == null)
            {
                AddProduct(product);
                return;
            }

            currentProduct.Update(product);

            Task.Run(() =>
            {
                _databaseAdapter.UpdateProductOld(currentProduct);
            });
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
            var newObject = _converter.Convert(productName, sensorValue);
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(productName))
                {
                    _productSensorsDictionary[productName] = new List<SensorInfo>();
                }
                _productSensorsDictionary[productName].Add(newObject);
            }

            Task.Run(() => _databaseAdapter.AddSensorOld(newObject));
        }

        public void AddSensorIfNotRegistered(string productName, SensorValueBase sensorValue)
        {
            bool needToAdd = false;
            var newObject = _converter.Convert(productName, sensorValue);
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
                Task.Run(() => _databaseAdapter.AddSensorOld(newObject));
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

            //ThreadPool.QueueUserWorkItem(_ => _databaseAdapter.AddSensor(sensorInfo));
            //ThreadPool.QueueUserWorkItem(_ =>
            //    _databaseAdapter.AddNewSensorToList(sensorInfo.ProductName, sensorInfo.Path));
            Task.Run(() => _databaseAdapter.AddSensorOld(sensorInfo));
        }
        public void RemoveSensor(string productName, string path)
        {
            try
            {
                lock (_dictionaryLock)
                {
                    var existingInfo = _productSensorsDictionary[productName].FirstOrDefault(s => s.Path == path);
                    if (existingInfo != null)
                    {
                        _productSensorsDictionary[productName].Remove(existingInfo);
                    }
                }

                Task.Run(() => _databaseAdapter.RemoveSensorOld(productName, path));
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Error while removing sensor {path} for {productName}");
            }
            
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

        public Product GetProductByKey(string key)
        {
            Product product = default(Product);
            lock (_productsLock)
            {
                product = _products.FirstOrDefault(p => p.Key.Equals(key));
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
