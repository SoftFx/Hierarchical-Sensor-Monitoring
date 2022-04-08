using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
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
        [Trait("Category", "OneProductRemove")]
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
        [Trait("Category", "SeveralProductRemove")]
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
        [Trait("Category", "OneExtraKey")]
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

        #region [ User Tests ]

        [Fact]
        [Trait("Category", "OneUser")]
        public void AddUser()
        {
            var name = RandomGenerator.GetRandomString();
            var user = DatabaseCoreFactory.CreateUser(name);
            _databaseCore.AddUser(user);

            var actualUser = GetUser(name);

            FullUserTest(user, actualUser);
        }

        [Fact]
        [Trait("Category", "OneUserRemove")]
        public void RemoveUser()
        {
            var name = RandomGenerator.GetRandomString();
            var user = DatabaseCoreFactory.CreateUser(name);

            _databaseCore.AddUser(user);
            _databaseCore.RemoveUser(user);

            var actualUser = GetUser(name);
            Assert.Null(actualUser);
        }

        [Fact]
        [Trait("Category", "OneProductRole")]
        public void AddProductRole()
        {
            var name = RandomGenerator.GetRandomString();
            var user = DatabaseCoreFactory.CreateUser(name);
            var product = DatabaseCoreFactory.CreateProduct(RandomGenerator.GetRandomString());

            _databaseCore.AddUser(user);
            user.ProductsRoles.Add(new KeyValuePair<string, ProductRoleEnum>(product.Key, ProductRoleEnum.ProductManager));
            _databaseCore.UpdateUser(user);

            var actualUser = GetUser(name);
            FullUserTest(user, actualUser);
        }

        [Theory]
        [InlineData(3, 0, 0)]
        [InlineData(10, 4, 3)]
        [InlineData(50, 52, 1)]
        [InlineData(100, 1, 100)]
        [InlineData(500, 2, 10)]
        [InlineData(1000, 2, 1)]
        [Trait("Category", "GetUsersPage")]
        public void GetUsersPageTest(int count, int page, int pageSize)
        {
            for (int i=0; i < count; i++)
            {
                var name = RandomGenerator.GetRandomString();
                var user = DatabaseCoreFactory.CreateUser(name);

                _databaseCore.AddUser(user);
            }

            var actualUsers = _databaseCore.GetUsersPage(page, pageSize);

            Assert.NotNull(actualUsers);
            Assert.Equal(GetTotalCount(count, page, pageSize), actualUsers.Count);
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
                    var expectedExtraKey = expectedProduct.ExtraKeys[i];
                    var actualExtraKey = actualProduct.ExtraKeys[i];

                    Assert.Equal(expectedExtraKey.Key, actualExtraKey.Key);
                    Assert.Equal(expectedExtraKey.Name, actualExtraKey.Name);
                }
            }
        }

        private static void FullUserTest(User expectedUser, User actualUser)
        {
            Assert.NotNull(actualUser);
            Assert.Equal(expectedUser.Id, actualUser.Id);
            Assert.Equal(expectedUser.UserName, actualUser.UserName);
            Assert.Equal(expectedUser.Password, actualUser.Password);
            Assert.Equal(expectedUser.IsAdmin, actualUser.IsAdmin);
            Assert.Equal(expectedUser.ProductsRoles.Count, actualUser.ProductsRoles.Count);

            if (expectedUser.ProductsRoles.Count > 0)
            {
                expectedUser.ProductsRoles.OrderBy(pr => pr.Key);
                actualUser.ProductsRoles.OrderBy(pr => pr.Key);

                for (int i=0; i < expectedUser.ProductsRoles.Count; i++)
                {
                    var expectedRole = expectedUser.ProductsRoles[i];
                    var actualRole = actualUser.ProductsRoles[i];

                    Assert.Equal(expectedRole.Key, actualRole.Key);
                    Assert.Equal(expectedRole.Value, actualRole.Value);
                }
            }
        }

        private User GetUser(string username) => 
            _databaseCore.GetUsers().FirstOrDefault(u => u.Equals(username));

        private int GetTotalCount(int count, int page, int pageSize)
        {
            count--;

            if (page == count / pageSize)
                return count % pageSize;
            else if (page > count / pageSize)
                return 0;
            else if (page < count / pageSize)
                return pageSize;

            return -1;
        }

        #endregion
    }
}
