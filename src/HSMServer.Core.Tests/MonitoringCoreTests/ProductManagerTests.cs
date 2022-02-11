using HSMCommon.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    public class ProductManagerTests : MonitoringCoreTestsBase<ProductManagerFixture>
    {
        private delegate string GetProductNameByKey(string key);
        private delegate Product GetProduct(string value);
        private readonly UserManager _userManager;

        private readonly User _defaultUser = TestUsersManager.DefaultUser;
        private readonly User _notAdminUser = TestUsersManager.NotAdmin;

        public ProductManagerTests(ProductManagerFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) {
            _userManager = new UserManager(_databaseAdapterManager.DatabaseAdapter, CommonMoqs.CreateNullLogger<UserManager>());
        }


        [Fact]
        [Trait("Category", "One")]
        public void AddProductTest()
        {
            var name = RandomGenerator.GetRandomString();

            _productManager.AddProduct(name);

            FullProductTest(name, _productManager.GetProductByName, _productManager.GetProductByKey,
                _productManager.GetProductNameByKey);
        }

        [Fact]
        [Trait("Category", "OneRemove")]
        public void RemoveProductTest()
        {
            var name = RandomGenerator.GetRandomString();

            _productManager.AddProduct(name);
            var key = _productManager.GetProductByName(name).Key;
            _productManager.RemoveProduct(name);

            FullRemoveProductTest(name, key, _productManager.GetProductByName, _productManager.GetProductByKey,
                _productManager.GetProductNameByKey);
        }

        [Fact]
        [Trait("Category", "OneUpdateExtraKey")]
        public async Task UpdateExtraProductKeyTest()
        {
            var name = RandomGenerator.GetRandomString();
            _productManager.AddProduct(name);
            var product = _productManager.GetProductByName(name);

            var extraKeyName = RandomGenerator.GetRandomString();
            product.AddExtraKey(extraKeyName);

            _productManager.UpdateProduct(product);

            await FullUpdateExtraProductKeyTest(product, _productManager.GetProductByName, _productManager.GetProductByKey);
        }

        [Fact]
        [Trait("Category", "GetCopy")]
        public void GetCopyProductTest()
        {
            var name = RandomGenerator.GetRandomString();
            _productManager.AddProduct(name);
            var product = _productManager.GetProductByName(name);

            TestGetProductCopy(product, _productManager.GetProductCopyByKey);
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

            names.ForEach(_productManager.AddProduct);

            FullSeveralProductsTest(names, _productManager.GetProductByName, _productManager.GetProductByKey,
                _productManager.GetProductNameByKey);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "GetSeveralAdmin")]
        public void GetSeveralAdminProductsTest(int count)
        {
            var names = GetRandomProductsNames(count);
            names.ForEach(_productManager.AddProduct);
            
            var products = _productManager.GetProducts(_defaultUser);
            names.Add(CommonConstants.SelfMonitoringProductName);
            names.Add(TestProductsManager.ProductName);

            Assert.Equal(names.Count, products.Count);

            FullGetSeveralProductsTest(products, _productManager.GetProductCopyByKey);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "GetSeveralNotAdmin")]
        public async Task GetSeveralNotAdminProductsTest(int count)
        {
            var names = GetRandomProductsNames(count);
            var total = 0;

            for (int i = 0; i< names.Count; i++)
            {
                _productManager.AddProduct(names[i]);
                if (i % 2 == 1) continue;

                var product = _productManager.GetProductByName(names[i]);
                _notAdminUser.ProductsRoles.Add(new KeyValuePair<string, ProductRoleEnum>(product.Key,
                    (ProductRoleEnum)RandomGenerator.GetRandomInt(min: 0, max: 2)));

                total++;
            }

            _userManager.UpdateUser(_notAdminUser);

            await Task.Delay(100);

            var products = _productManager.GetProducts(_notAdminUser);
            Assert.Equal(total, products.Count);

            FullGetSeveralProductsTest(products, _productManager.GetProductCopyByKey);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralUpdateExtraKeys")]
        public async Task UpdateSeveralExtraProductKeysTest(int count)
        {
            var name = RandomGenerator.GetRandomString();
            _productManager.AddProduct(name);
            var product = _productManager.GetProductByName(name);

            var extraKeyNames = GetRandomProductsNames(count);
            extraKeyNames.ForEach(product.AddExtraKey);

            _productManager.UpdateProduct(product);

            await FullUpdateExtraProductKeyTest(product, _productManager.GetProductByName, _productManager.GetProductByKey);
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
            names.ForEach(_productManager.AddProduct);

            var tuples = new List<(string name, string key)>(count);
            foreach (var name in names)
                tuples.Add((name, _productManager.GetProductByName(name).Key));

            names.ForEach(_productManager.RemoveProduct);

            FullSeveralRemoveProductsTest(tuples, _productManager.GetProductByName, _productManager.GetProductByKey,
                _productManager.GetProductNameByKey);
        }

        #region [Private methods]

        private static void FullProductTest(string name, GetProduct getProductByName,
            GetProduct getProductByKey, GetProductNameByKey getNameByKey)
        {
            var product = TestProductByName(name, getProductByName);
            TestProductByKey(name, product.Key, getProductByKey);
            TestProductNameByKey(product.Name, product.Key, getNameByKey);
        }

        private static void FullRemoveProductTest(string name, string key, GetProduct getProductByName,
            GetProduct getProductByKey, GetProductNameByKey getNameByKey)
        {
            TestRemoveProductByName(name, getProductByName);
            TestRemoveProductByKey(key, getProductByKey);
            TestRemoveProductNameByKey(key, getNameByKey);
        }

        private static async Task FullUpdateExtraProductKeyTest(Product product, GetProduct getProductByName,
            GetProduct getProductByKey)
        {
            await Task.Delay(100);

            TestExtraProductKeyByName(product, getProductByName);
            TestExtraProductKeyByKey(product, getProductByKey);
        }

        private static void FullSeveralProductsTest(List<string> names, GetProduct getProductByName,
            GetProduct getProductByKey, GetProductNameByKey getNameByKey)
        {
            for (int i = 0; i < names.Count; i++)
                FullProductTest(names[i], getProductByName, getProductByKey, getNameByKey);
        }

        private static void FullGetSeveralProductsTest(List<Product> products, GetProduct getProductByKey)
        {
            for (int i = 0; i < products.Count; i++)
                TestGetProductCopy(products[i], getProductByKey);
        }

        private static void FullSeveralRemoveProductsTest(List<(string name, string key)> tuples,
            GetProduct getProductByName, GetProduct getProductByKey, GetProductNameByKey getNameByKey)
        {
            for (int i = 0; i < tuples.Count; i++)
                FullRemoveProductTest(tuples[i].name, tuples[i].key, getProductByName, getProductByKey, getNameByKey);
        }

        private static Product TestProductByName(string name, GetProduct getProductByName)
        {
            var product = getProductByName?.Invoke(name);

            Assert.NotNull(product);
            Assert.Equal(name, product.Name);

            return product;
        }

        private static void TestRemoveProductByName(string name, GetProduct getProductByName)
        {
            var product = getProductByName?.Invoke(name);

            Assert.Null(product);
        }

        private static void TestExtraProductKeyByName(Product product, GetProduct getProductByName)
        {
            var updatedProduct = getProductByName?.Invoke(product.Name);

            TestExtraKeys(product, updatedProduct);
        }

        private static void TestGetProductCopy(Product product, GetProduct getProductByKey)
        {
            var copy = getProductByKey?.Invoke(product.Key);

            Assert.NotNull(copy);
            Assert.Equal(product.Name, copy.Name);
            Assert.Equal(product.Key, copy.Key);
            Assert.Equal(product.ExtraKeys.Count, copy.ExtraKeys.Count);
            Assert.Equal(product.DateAdded, copy.DateAdded);
            Assert.Equal(product.Sensors.Count, copy.Sensors.Count);
        }

        private static void TestProductByKey(string name, string key, GetProduct getProductByKey)
        {
            var product = getProductByKey?.Invoke(key);

            Assert.NotNull(product);
            Assert.Equal(key, product.Key);
            Assert.Equal(name, product.Name);
        }

        private static void TestExtraProductKeyByKey(Product product, GetProduct getProductByKey)
        {
            var updatedProduct = getProductByKey?.Invoke(product.Key);

            TestExtraKeys(product, updatedProduct);
        }

        private static void TestExtraKeys(Product product, Product updatedProduct)
        {
            Assert.Equal(product.ExtraKeys.Count, updatedProduct.ExtraKeys.Count);

            product.ExtraKeys.OrderBy(ek => ek.Name);
            updatedProduct.ExtraKeys.OrderBy(ek => ek.Name);

            for (int i = 0; i < updatedProduct.ExtraKeys.Count; i++)
            {
                var expectedKey = product.ExtraKeys[i];
                var updatedKey = updatedProduct.ExtraKeys[i];

                Assert.NotNull(updatedKey);
                Assert.Equal(expectedKey.Name, updatedKey.Name);
                Assert.Equal(expectedKey.Key, updatedKey.Key);
            }
        }

        private static void TestRemoveProductByKey(string key, GetProduct getProductByKey)
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
                names.Add(RandomGenerator.GetRandomString());

            return names;
        }

        #endregion
    }
}
