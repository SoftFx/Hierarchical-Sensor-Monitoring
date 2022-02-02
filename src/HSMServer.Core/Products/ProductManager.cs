using HSMCommon.Constants;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;
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

        public List<Product> Products => _products.Values.ToList();

        public Product GetProductByName(string name) =>
            _products.GetValueOrDefault(name);
            
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
            Product product = new (CommonConstants.SelfMonitoringProductKey,
                CommonConstants.SelfMonitoringProductName, DateTime.Now);

            AddProduct(product);
        }

        public void RemoveProduct(string name)
        {
            try
            {
                _databaseAdapter.RemoveProduct(name);

                if (GetProductByName(name) != null)
                {
                    _products.Remove(name, out _);
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

            Product product = new (key, name, DateTime.Now);
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

        public string GetProductNameByKey(string key) =>
            GetProductByKey(key)?.Name;

        public Product GetProductByKey(string key)
        {
            foreach (var (_, product) in _products)
            {
                if (product.Key.Equals(key)) return product;
            }

            return null;
        }
    }
}
