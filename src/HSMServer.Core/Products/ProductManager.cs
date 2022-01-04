using HSMCommon.Constants;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Converters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Core.Products
{
    public class ProductManager : IProductManager
    {
        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly ILogger<ProductManager> _logger;
        private readonly List<Product> _products;
        private readonly Dictionary<string, Dictionary<string, SensorInfo>> _productSensorsDictionary =
            new Dictionary<string, Dictionary<string, SensorInfo>>();
        private readonly object _productsLock = new object();
        private readonly object _dictionaryLock = new object();
        public ProductManager(IDatabaseAdapter databaseAdapter, ILogger<ProductManager> logger)
        {
            _logger = logger;
            _databaseAdapter = databaseAdapter;
            _products = new List<Product>();
            //MigrateProductsToNewDatabase();
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
        //private void MigrateProductsToNewDatabase()
        //{
        //    var existingProducts = _databaseAdapter.GetProductsOld();
        //    foreach (var product in existingProducts)
        //    {
        //        var sensors = _databaseAdapter.GetProductSensorsOld(product);
        //        _databaseAdapter.AddProduct(product);
        //        foreach (var sensor in sensors)
        //        {
        //            _databaseAdapter.AddSensor(sensor);
        //        }
        //    }
        //}
        //TODO: read products via product info, like users are read now
        private void InitializeProducts()
        {
            int count = 0;
            //var existingProducts = _databaseAdapter.GetProductsOld();
            var existingProducts = _databaseAdapter.GetProducts();
            foreach (var product in existingProducts)
            {
                lock (_productsLock)
                {
                    _products.Add(product);
                }
                //var sensors = _databaseAdapter.GetProductSensorsOld(product);
                var sensors = _databaseAdapter.GetProductSensors(product);
                lock (_dictionaryLock)
                {
                    _productSensorsDictionary[product.Name] = new Dictionary<string, SensorInfo>();
                    foreach (var sensor in sensors)
                    {
                        _productSensorsDictionary[product.Name].Add(sensor.Path, sensor);
                    }
                }

                ++count;
            }

            var monitoringProduct = GetProductByName(CommonConstants.SelfMonitoringProductName);
            if (count < 1 || monitoringProduct == null)
            {
                AddSelfMonitoringProduct();
            }

            lock (_productsLock)
            {
                _logger.LogInformation($"{_products.Count} products read, ProductManager initialized");
            }

        }

        private void AddSelfMonitoringProduct()
        {
            //Product product = new Product(CommonConstants.DefaultProductKey, CommonConstants.DefaultProductName, 
            //    DateTime.Now);
            Product product = new Product(CommonConstants.SelfMonitoringProductKey,
                CommonConstants.SelfMonitoringProductName, DateTime.Now);
            AddProduct(product);
        }

        public void RemoveProduct(string name)
        {
            try
            {
                //_databaseAdapter.RemoveProductOld(name);
                _databaseAdapter.RemoveProduct(name);
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
                //_databaseAdapter.AddProductOld(product);
                _databaseAdapter.AddProduct(product);
                lock (_productsLock)
                {
                    _products.Add(product);
                }

                lock (_dictionaryLock)
                {
                    _productSensorsDictionary[product.Name] = new Dictionary<string, SensorInfo>();
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
                //_databaseAdapter.UpdateProductOld(currentProduct);
                _databaseAdapter.UpdateProduct(currentProduct);
            });
        }

        public List<SensorInfo> GetAllExistingSensorInfos()
        {
            List<SensorInfo> result = new List<SensorInfo>();
            lock (_dictionaryLock)
            {
                foreach (var pair in _productSensorsDictionary)
                {
                    result.AddRange(pair.Value.Values);
                }
            }

            return result;
        }

        public void UpdateSensorInfo(SensorInfo newInfo)
        {
            var existingInfo = GetSensorInfo(newInfo.ProductName, newInfo.Path);
            existingInfo.Update(newInfo);
            Task.Run(() => _databaseAdapter.UpdateSensor(existingInfo));
        }

        public bool IsSensorRegistered(string productName, string path)
        {
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(productName))
                    return false;

                return _productSensorsDictionary[productName].ContainsKey(path);
            }
        }

        public void AddSensor(string productName, SensorValueBase sensorValue)
        {
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(productName))
                {
                    _productSensorsDictionary[productName] = new Dictionary<string, SensorInfo>();
                }

                var newSensor = sensorValue.Convert(productName);

                if (!_productSensorsDictionary[productName].ContainsKey(newSensor.Path))
                {
                    _productSensorsDictionary[productName].Add(newSensor.Path, newSensor);
                    _databaseAdapter.AddSensor(newSensor);
                    //Task.Run(() => _databaseAdapter.AddSensor(newSensor));
                }
            }

            //Task.Run(() => _databaseAdapter.AddSensorOld(newObject));
        }

        public void AddSensorIfNotRegistered(string productName, SensorValueBase sensorValue)
        {
            bool needToAdd = false;
            var newObject = sensorValue.Convert(productName);
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(productName))
                {
                    _productSensorsDictionary[productName] = new Dictionary<string, SensorInfo>();
                    needToAdd = true;
                }

                var doesSensorExist = _productSensorsDictionary[productName]
                    .TryGetValue(sensorValue.Path, out var existingSensor);
                if (!doesSensorExist || !string.IsNullOrEmpty(sensorValue.Description))
                {
                    _productSensorsDictionary[productName].Add(newObject.Path, newObject);
                    needToAdd = true;
                }
            }

            if (needToAdd)
            {
                //Task.Run(() => _databaseAdapter.AddSensorOld(newObject));
                Task.Run(() => _databaseAdapter.AddSensor(newObject));
            }
        }

        public void AddSensor(SensorInfo sensorInfo)
        {
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(sensorInfo.ProductName))
                {
                    _productSensorsDictionary[sensorInfo.ProductName] = new Dictionary<string, SensorInfo>();
                }
                _productSensorsDictionary[sensorInfo.ProductName].Add(sensorInfo.Path, sensorInfo);
            }

            //Task.Run(() => _databaseAdapter.AddSensorOld(sensorInfo));
            Task.Run(() => _databaseAdapter.AddSensor(sensorInfo));
        }
        public void RemoveSensor(string productName, string path)
        {
            try
            {
                lock (_dictionaryLock)
                {
                    var existingInfo = _productSensorsDictionary[productName][path];
                    if (existingInfo != null)
                    {
                        _productSensorsDictionary[productName].Remove(existingInfo.Path);
                    }
                }

                //Task.Run(() => _databaseAdapter.RemoveSensorOld(productName, path));
                Task.Run(() => _databaseAdapter.RemoveSensor(productName, path));
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
                result.AddRange(pair.Value.Values);
            }

            return result;
        }

        public SensorInfo GetSensorInfo(string productName, string path)
        {
            SensorInfo result = default(SensorInfo);
            lock (_dictionaryLock)
            {
                var dict = _productSensorsDictionary[productName];
                dict.TryGetValue(path, out result);
            }
            return result;
        }
    }
}
