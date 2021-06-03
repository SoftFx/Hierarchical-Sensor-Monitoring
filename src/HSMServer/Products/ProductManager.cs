using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.Constants;
using HSMServer.Keys;
using HSMServer.DataLayer;
using HSMServer.DataLayer.Model;
using NLog;
using Product = HSMServer.DataLayer.Model.Product;

namespace HSMServer.Products
{
    public class ProductManager : IProductManager
    {
        private readonly IDatabaseClass _database;
        private readonly Logger _logger;
        private readonly List<Product> _products;
        private readonly Dictionary<string, List<SensorInfo>> _productSensorsDictionary = new Dictionary<string, List<SensorInfo>>();
        private readonly object _productsLock = new object();
        private readonly object _dictionaryLock = new object();
        public ProductManager(IDatabaseClass database)
        {
            _logger = LogManager.GetCurrentClassLogger();
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
                    sensorInfos.Add(_database.GetSensorInfo(productName, sensorPath));
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
                _logger.Info($"{_products.Count} products read, ProductManager initialized");
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
                _logger.Error(e, $"Failed to remove product, name = {name}");
            }
        }
        public void AddProduct(string name)
        {
            string key = KeyGenerator.GenerateProductKey(name);
            _logger.Info($"Created product key = '{key}' for product = '{name}'");
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
                _logger.Error(e, $"Failed to add new product, name = {product.Name}");
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

        public void AddSensor(string productName, string path)
        {
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(productName))
                {
                    _productSensorsDictionary[productName] = new List<SensorInfo>();
                }
                _productSensorsDictionary[productName].Add(path);
            }

            Task.Run(() => _database.AddNewSensorToList(productName, path));
        }

        public void AddSensorIfNotRegistered(string productName, string path)
        {
            bool needToAdd = false;
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(productName))
                {
                    _productSensorsDictionary[productName] = new List<string>();
                    needToAdd = true;
                }

                if (!_productSensorsDictionary[productName].Contains(path))
                {
                    _productSensorsDictionary[productName].Add(path);
                    needToAdd = true;
                }
            }

            if (needToAdd)
            {
                Task.Run(() => _database.AddNewSensorToList(productName, path));
            }
        }

        public void AddSensor(SensorInfo sensorInfo)
        {
            lock (_dictionaryLock)
            {
                if (!_productSensorsDictionary.ContainsKey(sensorInfo.ProductName))
                {
                    _productSensorsDictionary[sensorInfo.ProductName] = new List<string>();
                }
                _productSensorsDictionary[sensorInfo.ProductName].Add(sensorInfo.Path);
            }

            //ThreadPool.QueueUserWorkItem(_ => _database.AddSensor(sensorInfo));
            //ThreadPool.QueueUserWorkItem(_ =>
            //    _database.AddNewSensorToList(sensorInfo.ProductName, sensorInfo.Path));
            Task.Run(() => _database.AddNewSensorToList(sensorInfo.ProductName, sensorInfo.Path));
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

        public List<string> GetProductSensors(string productName)
        {
            List<string> result = new List<string>();
            lock (_dictionaryLock)
            {
                var pair = _productSensorsDictionary.FirstOrDefault(p => p.Key == productName);
                result.AddRange(pair.Value);
            }

            return result;
        }
    }
}
