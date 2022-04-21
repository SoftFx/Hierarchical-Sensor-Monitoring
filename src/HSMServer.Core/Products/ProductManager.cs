using HSMCommon.Constants;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Helpers;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
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

        public List<Product> Products => _products.Values.ToList();

        public event Action<Product> RemovedProduct;

        public ProductManager(IDatabaseAdapter databaseAdapter, ILogger<ProductManager> logger)
        {
            _logger = logger;
            _databaseAdapter = databaseAdapter;
            _products = new ConcurrentDictionary<string, Product>();

            InitializeProducts();
        }

        public Product GetProductByName(string name) =>
            _products.GetValueOrDefault(name);

        public Product GetProductCopyByKey(string key)
        {
            var product = GetProductByKey(key);
            if (product == null)
                _logger.LogError($"Failed to find the product with key {key}");

            return new Product(product);
        }

        private void InitializeProducts()
        {
            var existingProducts = _databaseAdapter.GetProducts();
            foreach (var product in existingProducts)
            {
                _products[product.DisplayName] = product;

                var sensors = _databaseAdapter.GetProductSensors(product);
                product.InitializeSensors(sensors);
            }

            var monitoringProduct = GetProductByName(CommonConstants.SelfMonitoringProductName);
            if (existingProducts.Count < 1 || monitoringProduct == null)
            {
                AddSelfMonitoringProduct();
            }

            _logger.LogInformation($"{_products.Count} products read, ProductManager initialized");
        }

        private void AddSelfMonitoringProduct()
        {
            Product product = new (CommonConstants.SelfMonitoringProductKey, CommonConstants.SelfMonitoringProductName);

            AddProduct(product);
        }

        public void AddProduct(ProductModel product) =>
            _products[product.DisplayName] = new Product() 
            { 
                Id = product.Id.ToString(),
                DisplayName = product.DisplayName
            };

        public Product AddProduct(string name)
        {
            string key = KeyGenerator.GenerateProductKey(name);

            _logger.LogInformation($"Created product key = '{key}' for product = '{name}'");

            Product product = new (key, name);
            AddProduct(product);

            return product;
        }

        private void AddProduct(Product product)
        {
            try
            {
                _databaseAdapter.AddProduct(product);

                _products[product.DisplayName] = product;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to add new product, name = {product.DisplayName}");
            }
        }

        public void UpdateProduct(Product product)
        {
            Product currentProduct = _products[product.DisplayName];

            if (currentProduct == null)
            {
                AddProduct(product);
                return;
            }

            currentProduct.Update(product);
            _databaseAdapter.UpdateProduct(currentProduct);
        }

        public void RemoveProduct(string name)
        {
            try
            {
                if (GetProductByName(name) == null) 
                    return;

                _databaseAdapter.RemoveProduct(name);
                _products.Remove(name, out var product);

                RemovedProduct?.Invoke(product);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to remove product, name = {name}");
            }
        }

        public string GetProductNameByKey(string key) =>
            GetProductByKey(key)?.DisplayName;

        public Product GetProductByKey(string key)
        {
            foreach (var (_, product) in _products)
            {
                if (product.Id.Equals(key)) 
                    return product;
            }

            return null;
        }

        public List<Product> GetProducts(User user)
        {
            if (user.IsAdmin) 
                return Products;

            if (user.ProductsRoles == null || user.ProductsRoles.Count == 0)
                return null;

            return Products.Where(p =>
                ProductRoleHelper.IsAvailable(p.Id, user.ProductsRoles)).ToList();
        }
    }
}
