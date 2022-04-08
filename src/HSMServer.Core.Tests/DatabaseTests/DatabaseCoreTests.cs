using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.DatabaseTests.Fixture;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests
{
    public class DatabaseCoreTests : DatabaseCoreTestsBase<DatabaseCoreFixture>
    {
        private readonly IDatabaseCore _databaseCore;
        private readonly DatabaseCoreFixture _fixture;

        public DatabaseCoreTests(DatabaseCoreFixture fixture, DatabaseRegisterFixture registerFixture) 
            : base(fixture, registerFixture)
        {
            _databaseCore = _databaseCoreManager.DatabaseCore;
            _fixture = fixture;
        }

        #region [ Product Tests ]

        [Fact]
        [Trait("Category", "OneProduct")]
        public void AddProductTest()
        {
            var name = RandomGenerator.GetRandomString();
            var product = DatabaseCoreFactory.CreateProduct(name);
            _databaseCore.AddProduct(product);

            FullProductTest(product, _databaseCore.GetProduct(name));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralProduct")]
        public void AddSeveralProductsTest(int count)
        {
            for (int i=0; i < count; i++)
            {
                var name = RandomGenerator.GetRandomString();
                var product = DatabaseCoreFactory.CreateProduct(name);
                _databaseCore.AddProduct(product);

                FullProductTest(product, _databaseCore.GetProduct(name));
            }
        }

        [Fact]
        [Trait("Category", "OneRemoveProduct")]
        public void RemoveProductTest()
        {
            var name = RandomGenerator.GetRandomString();
            var product = DatabaseCoreFactory.CreateProduct(name);

            _databaseCore.AddProduct(product);
            Assert.NotNull(_databaseCore.GetProduct(name));
            
            _databaseCore.RemoveProduct(name);
            Assert.Null(_databaseCore.GetProduct(name));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "RemoveSeveralProduct")]
        public void RemoveSeveralProductsTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var name = RandomGenerator.GetRandomString();
                var product = DatabaseCoreFactory.CreateProduct(name);

                _databaseCore.AddProduct(product);
                Assert.NotNull(_databaseCore.GetProduct(name));

                _databaseCore.RemoveProduct(name);
                Assert.Null(_databaseCore.GetProduct(name));
            }
        }

        [Fact]
        [Trait("Category", "OneExtraKeyAdd")]
        public void AddExtraKeyTest()
        {
            var name = RandomGenerator.GetRandomString();
            var product = DatabaseCoreFactory.CreateProduct(name);
            var extraKey = DatabaseCoreFactory.CreateExtraKey(name, RandomGenerator.GetRandomString());
            product.ExtraKeys = new List<ExtraProductKey> { extraKey };

            _databaseCore.UpdateProduct(product);

            FullProductTest(product, _databaseCore.GetProduct(name));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralExtraKey")]
        public void AddSeveralExtraKeyTest(int count)
        {
            var name = RandomGenerator.GetRandomString();
            var product = DatabaseCoreFactory.CreateProduct(name);
            var extraKeys = new List<ExtraProductKey>(count);

            for (int i = 0; i < count; i++)
            {
                extraKeys.Add(DatabaseCoreFactory.CreateExtraKey(name, RandomGenerator.GetRandomString()));
            }

            product.ExtraKeys = extraKeys;
            _databaseCore.UpdateProduct(product);

            FullProductTest(product, _databaseCore.GetProduct(name));
        }

        #endregion

        #region [ Private methods ]

        private static void FullProductTest(Product expectedProduct, Product actualProduct)
        {
            Assert.NotNull(actualProduct);
            Assert.Equal(expectedProduct.Name, actualProduct.Name);
            Assert.Equal(expectedProduct.Key, actualProduct.Key);
            Assert.Equal(expectedProduct.DateAdded, actualProduct.DateAdded);
            Assert.Equal(expectedProduct.ExtraKeys.Count, actualProduct.ExtraKeys.Count);

            if (expectedProduct.ExtraKeys.Count > 0)
            {
                expectedProduct.ExtraKeys.OrderBy(ek => ek.Name);
                actualProduct.ExtraKeys.OrderBy(ek => ek.Name);

                for (int i=0; i < expectedProduct.ExtraKeys.Count; i++)
                {
                    Assert.Equal(expectedProduct.ExtraKeys[i].Key, actualProduct.ExtraKeys[i].Key);
                    Assert.Equal(expectedProduct.ExtraKeys[i].Name, actualProduct.ExtraKeys[i].Name);
                }
            }
        }

        #endregion
    }
}
