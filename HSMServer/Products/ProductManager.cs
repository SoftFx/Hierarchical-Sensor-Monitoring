using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Keys;
using HSMServer.DataLayer;
using NLog;
using Product = HSMServer.DataLayer.Model.Product;

namespace HSMServer.Products
{
    public class ProductManager
    {
        private readonly Logger _logger;
        private List<Product> _products;
        private object _accessLock = new object();
        public ProductManager()
        {
            _logger = LogManager.GetCurrentClassLogger();
            _products = new List<Product>();
            InitializeProducts();
        }

        private void InitializeProducts()
        {
            List<string> productNames = DatabaseClass.Instance.GetProductsList();
            foreach (var productName in productNames)
            {
                var info = DatabaseClass.Instance.GetProductInfo(productName);
                if (info != null)
                {
                    lock (_accessLock)
                    {
                        _products.Add(info);
                    }
                    
                }
            }

            lock (_accessLock)
            {
                _logger.Info($"{_products.Count} products read");
            }
            
        }

        public void AddProduct(string name)
        {
            string key = KeyGenerator.GenerateProductKey(name);
            Product product = new Product {Key = key, Name = name, DateAdded = DateTime.Now};
            try
            {
                DatabaseClass.Instance.AddProductToList(name);
                DatabaseClass.Instance.PutProductInfo(product);
                lock (_accessLock)
                {
                    _products.Add(product);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to add new product, name = {name}");
            }
        }

        public string GetProductKeyByName(string name)
        {
            Product product = null;
            lock (_accessLock)
            {
                product = _products.FirstOrDefault(p => p.Name.Equals(name));
            }
            return product?.Key;
        }

        public string GetProductNameByKey(string key)
        {
            Product product = null;
            lock (_accessLock)
            {
                product = _products.FirstOrDefault(p => p.Key.Equals(key));
            }
            return product?.Name;
        }
    }
}
