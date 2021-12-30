using HSMCommon;
using HSMServer.Core.Model;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringDataReceiverTests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    public class ProductManagerTests : IDisposable
    {
        private readonly IProductManager _productManager;
        private readonly DatabaseAdapterManager _databaseAdapterManager;

        private delegate string GetProductNameByKey(string key);
        private delegate Product GetProductByName(string name);
        private delegate Product GetProductByKey(string key);

        public ProductManagerTests()
        {
            var converterLogger = CommonMoqs.CreateNullLogger<Converter>();
            var converter = new Converter(converterLogger);

            _databaseAdapterManager = new DatabaseAdapterManager();
            _databaseAdapterManager.AddTestProduct();

            var productManagerLogger = CommonMoqs.CreateNullLogger<ProductManager>();
            _productManager = new ProductManager(_databaseAdapterManager.DatabaseAdapter, converter, productManagerLogger);
        }

        [Fact]
        [Trait("Category", "One")]
        public void AddProductTest()
        {
            var name = RandomValuesGenerator.GetRandomString();

            _productManager.AddProduct(name);

            FullProductTest(name, _productManager.GetProductByName,
                _productManager.GetProductByKey, _productManager.GetProductNameByKey);
        }

        [Fact]
        [Trait("Category", "OneRemove")]
        public void RemoveProductTest()
        {
            var name = RandomValuesGenerator.GetRandomString();

            _productManager.AddProduct(name);
            var key = _productManager.GetProductByName(name).Key;
            _productManager.RemoveProduct(name);

            FullRemoveProductTest(name, key, _productManager.GetProductByName,
                _productManager.GetProductByKey, _productManager.GetProductNameByKey);
        }

        [Fact]
        [Trait("Category", "OneUpdateExtraKey")]
        public void UpdateExtraProductKeyTest()
        {
            var name = RandomValuesGenerator.GetRandomString();
            _productManager.AddProduct(name);
            var product = _productManager.GetProductByName(name);

            var extraKeyName = RandomValuesGenerator.GetRandomString();
            product.AddExtraKey(extraKeyName);

            _productManager.UpdateProduct(product);

            FullUpdateExtraProductKeyTest(product, _productManager.GetProductByName, _productManager.GetProductByKey);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "Several")]
        public void AddSeveralProductsTest(int count)
        {
            var names = GetRandomProductsNames(count);

            names.ForEach(n => _productManager.AddProduct(n));

            FullSeveralProductsTest(names, _productManager.GetProductByName,
                _productManager.GetProductByKey, _productManager.GetProductNameByKey);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralUpdateExtraKeys")]
        public void UpdateSeveralExtraProductKeysTest(int count)
        {
            var name = RandomValuesGenerator.GetRandomString();
            _productManager.AddProduct(name);
            var product = _productManager.GetProductByName(name);

            var extraKeyNames = GetRandomProductsNames(count);
            extraKeyNames.ForEach(ekn => product.AddExtraKey(ekn));
            
            _productManager.UpdateProduct(product);

            FullUpdateExtraProductKeyTest(product, _productManager.GetProductByName, _productManager.GetProductByKey);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "RemoveSeveral")]
        public void RemoveSeveralProductsTest(int count)
        {
            var names = GetRandomProductsNames(count);
            names.ForEach(n => _productManager.AddProduct(n));

            var pairs = new List<KeyValuePair<string, string>>(count);
            foreach (var name in names)
                pairs.Add(new KeyValuePair<string, string>(name, _productManager.GetProductByName(name).Key));

            names.ForEach(n => _productManager.RemoveProduct(n));

            FullSeveralRemoveProductsTest(pairs, _productManager.GetProductByName, _productManager.GetProductByKey,
                _productManager.GetProductNameByKey);
        }

        #region [Private methods]

        private static void FullProductTest(string name, GetProductByName getProductByName,
            GetProductByKey getProductByKey, GetProductNameByKey getNameByKey)
        {
            var product = TestProductByName(name, getProductByName);
            TestProductByKey(name, product.Key, getProductByKey);
            TestProductNameByKey(product.Name, product.Key, getNameByKey);
        }

        private static void FullRemoveProductTest(string name, string key, GetProductByName getProductByName,
            GetProductByKey getProductByKey, GetProductNameByKey getNameByKey)
        {
            TestRemoveProductByName(name, getProductByName);
            TestRemoveProductByKey(key, getProductByKey);
            TestRemoveProductNameByKey(key, getNameByKey);
        }
        private async static void FullUpdateExtraProductKeyTest(Product product, GetProductByName getProductByName,
            GetProductByKey getProductByKey)
        {
            await Task.Delay(100);

            TestExtraProductKeyByName(product, getProductByName);
            TestExtraProductKeyByKey(product, getProductByKey);
        }

        private static void FullSeveralProductsTest(List<string> names, GetProductByName getProductByName,
            GetProductByKey getProductByKey, GetProductNameByKey getNameByKey)
        {
            for (int i = 0; i < names.Count; i++)
                FullProductTest(names[i], getProductByName, getProductByKey, getNameByKey);
        }

        private static void FullSeveralRemoveProductsTest(List<KeyValuePair<string, string>> pairs,
            GetProductByName getProductByName, GetProductByKey getProductByKey, GetProductNameByKey getNameByKey)
        {
            for (int i = 0; i < pairs.Count; i++)
                FullRemoveProductTest(pairs[i].Key, pairs[i].Value, getProductByName, getProductByKey, getNameByKey);
        }

        private static Product TestProductByName(string name, GetProductByName getProductByName)
        {
            var product = getProductByName?.Invoke(name);

            Assert.NotNull(product);
            Assert.Equal(name, product.Name);

            return product;
        }

        private static void TestRemoveProductByName(string name, GetProductByName getProductByName)
        {
            var product = getProductByName?.Invoke(name);

            Assert.Null(product);
        }

        private static void TestExtraProductKeyByName(Product product, GetProductByName getProductByName)
        {
            var updatedProduct = getProductByName?.Invoke(product.Name);

            TestExtraKeys(product, updatedProduct);
        }

        private static void TestProductByKey(string name, string key, GetProductByKey getProductByKey)
        {
            var product = getProductByKey?.Invoke(key);

            Assert.NotNull(product);
            Assert.Equal(key, product.Key);
            Assert.Equal(name, product.Name);
        }

        private static void TestExtraProductKeyByKey(Product product, GetProductByKey getProductByKey)
        {
            var updatedProduct = getProductByKey?.Invoke(product.Key);

            TestExtraKeys(product, updatedProduct);
        }

        private static void TestExtraKeys(Product product, Product updatedProduct)
        {
            Assert.Equal(product.ExtraKeys.Count, updatedProduct.ExtraKeys.Count);

            for (int i = 0; i < updatedProduct.ExtraKeys.Count; i++)
            {
                var expectedKey = product.ExtraKeys.FirstOrDefault(ek => ek.Equals(updatedProduct.ExtraKeys[i].Name));

                Assert.NotNull(expectedKey);
                Assert.Equal(expectedKey.Name, updatedProduct.ExtraKeys[i].Name);
                Assert.Equal(expectedKey.Key, updatedProduct.ExtraKeys[i].Key);
            }
        }

        private static void TestRemoveProductByKey(string key, GetProductByKey getProductByKey)
        {
            var product = getProductByKey?.Invoke(key);

            Assert.Null(product);
        }

        private static void TestProductNameByKey(string productName, string key, GetProductNameByKey getProductNameByKey)
        {
            var name = getProductNameByKey?.Invoke(key);

            Assert.NotNull(name);
            Assert.Equal(productName, name);
        }

        private static void TestRemoveProductNameByKey(string key, GetProductNameByKey getProductNameByKey)
        {
            var name = getProductNameByKey?.Invoke(key);

            Assert.Null(name);
        }

        private static List<string> GetRandomProductsNames(int count)
        {
            var names = new List<string>(count);
            for (int i = 0; i < count; ++i)
                names.Add(RandomValuesGenerator.GetRandomString());

            return names;
        }

        #endregion

        public void Dispose()
        {
            _databaseAdapterManager.ClearDatabase();
            FileManager.SafeRemoveFolder(DatabaseAdapterManager.DatabaseFolder);
        }

    }
}
