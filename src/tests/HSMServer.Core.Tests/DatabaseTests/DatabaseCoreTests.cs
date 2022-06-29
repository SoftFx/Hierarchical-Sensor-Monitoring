using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.DatabaseTests.Fixture;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests
{
    public class DatabaseCoreTests : DatabaseCoreTestsBase<DatabaseCoreFixture>
    {
        private readonly IDatabaseCore _databaseCore;

        private delegate void AddAccessKey(AccessKeyEntity entity);

        public DatabaseCoreTests(DatabaseCoreFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _databaseCore = _databaseCoreManager.DatabaseCore;
        }

        #region [ Product Tests ]

        [Fact]
        [Trait("Category", "OneProduct")]
        public void AddProductTest()
        {
            var product = DatabaseCoreFactory.CreateProduct();
            _databaseCore.AddProduct(product);

            FullProductTest(product, _databaseCore.GetProduct(product.Id));
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
            for (int i = 0; i < count; i++)
            {
                var product = DatabaseCoreFactory.CreateProduct();
                _databaseCore.AddProduct(product);

                FullProductTest(product, _databaseCore.GetProduct(product.Id));
            }
        }

        [Fact]
        [Trait("Category", "OneProductRemove")]
        public void RemoveProductTest()
        {
            var product = DatabaseCoreFactory.CreateProduct();

            _databaseCore.AddProduct(product);
            Assert.NotNull(_databaseCore.GetProduct(product.Id));

            _databaseCore.RemoveProduct(product.Id);
            Assert.Null(_databaseCore.GetProduct(product.Id));
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
                var product = DatabaseCoreFactory.CreateProduct();

                _databaseCore.AddProduct(product);
                Assert.NotNull(_databaseCore.GetProduct(product.Id));

                _databaseCore.RemoveProduct(product.Id);
                Assert.Null(_databaseCore.GetProduct(product.Id));
            }
        }

        #endregion

        #region [ AccessKey Tests ]

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "Add access key(s)")]
        public void AddKeysTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var key = AddKey(_databaseCore.AddAccessKey);

                FullKeyTest(key, _databaseCore.GetAccessKey(Guid.Parse(key.Id)));
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "Remove access key(s)")]
        public void RemoveAccessKeysTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var key = AddKey(_databaseCore.AddAccessKey);
                var id = Guid.Parse(key.Id);

                Assert.NotNull(_databaseCore.GetAccessKey(id));

                _databaseCore.RemoveAccessKey(id);
                Assert.Null(_databaseCore.GetAccessKey(id));
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "Update access key(s)")]
        public void UpdateKeysTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var key = AddKey(_databaseCore.AddAccessKey);

                var updated = DatabaseCoreFactory.CreateAccessKey(key.Id);
                _databaseCore.UpdateAccessKey(updated);

                FullKeyTest(updated, _databaseCore.GetAccessKey(Guid.Parse(key.Id)));
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "GetAll access key(s)")]
        public void GetAllKeysTest(int count)
        {
            var expectedList = new List<AccessKeyEntity>(count);
            for (int i = 0; i < count; i++)
                expectedList.Add(AddKey(_databaseCore.AddAccessKey));

            var actualList = _databaseCore.GetAccessKeys();

            Assert.NotNull(actualList);
            Assert.Equal(expectedList.Count, actualList.Count);
            
            for (int i = 0; i < actualList.Count; i++)
                FullKeyTest(expectedList[i], actualList[i]);
        }

        #endregion

        #region [ User Tests ]

        [Fact]
        [Trait("Category", "OneUser")]
        public void AddUserTest()
        {
            var user = DatabaseCoreFactory.CreateUser();
            _databaseCore.AddUser(user);

            var actualUser = GetUser(user.UserName);

            FullUserTest(user, actualUser);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralUser")]
        public void AddSeveralUsersTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var user = DatabaseCoreFactory.CreateUser();
                _databaseCore.AddUser(user);

                var actualUser = GetUser(user.UserName);

                FullUserTest(user, actualUser);
            }
        }

        [Fact]
        [Trait("Category", "OneUserRemove")]
        public void RemoveUserTest()
        {
            var user = DatabaseCoreFactory.CreateUser();

            _databaseCore.AddUser(user);
            _databaseCore.RemoveUser(user);

            Assert.Null(GetUser(user.UserName));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralUsersRemove")]
        public void RemoveSeveralUsersTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var user = DatabaseCoreFactory.CreateUser();

                _databaseCore.AddUser(user);
                _databaseCore.RemoveUser(user);

                Assert.Null(GetUser(user.UserName));
            }
        }

        [Fact]
        [Trait("Category", "OneProductRole")]
        public void AddProductRoleTest()
        {
            var user = DatabaseCoreFactory.CreateUser();
            var product = DatabaseCoreFactory.CreateProduct();

            _databaseCore.AddUser(user);
            user.ProductsRoles.Add(new KeyValuePair<string, ProductRoleEnum>(product.Id,
                ProductRoleEnum.ProductManager));
            _databaseCore.UpdateUser(user);

            FullUserTest(user, GetUser(user.UserName));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralProductRoles")]
        public void SeveralProductRolesTest(int count)
        {
            var user = DatabaseCoreFactory.CreateUser();
            _databaseCore.AddUser(user);

            for (int i = 0; i < count; i++)
            {
                var product = DatabaseCoreFactory.CreateProduct();

                var role = i % 2 == 0 ? ProductRoleEnum.ProductManager : ProductRoleEnum.ProductViewer;
                user.ProductsRoles.Add(new KeyValuePair<string, ProductRoleEnum>(product.Id, role));
            }

            _databaseCore.UpdateUser(user);
            var actualUser = GetUser(user.UserName);
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
            for (int i = 0; i < count; i++)
            {
                var user = DatabaseCoreFactory.CreateUser();

                _databaseCore.AddUser(user);
            }

            var actualUsers = _databaseCore.GetUsersPage(page, pageSize);

            Assert.NotNull(actualUsers);
            Assert.Equal(GetCountItemsOnPage(count, page, pageSize), actualUsers.Count);
        }

        #endregion

        #region [ Registration Ticket ]

        [Fact]
        [Trait("Category", "OneRegistrationTicket")]
        public void AddRegistrationTicketTest()
        {
            var ticket = DatabaseCoreFactory.CreateTicket();

            _databaseCore.WriteRegistrationTicket(ticket);

            FullTicketTest(ticket, _databaseCore.ReadRegistrationTicket(ticket.Id));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralRegistrationTicket")]
        public void SeveralRegistartionTicketTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var ticket = DatabaseCoreFactory.CreateTicket();

                _databaseCore.WriteRegistrationTicket(ticket);

                FullTicketTest(ticket, _databaseCore.ReadRegistrationTicket(ticket.Id));
            }
        }

        [Fact]
        [Trait("Category", "OneRemoveRegistrationTicket")]
        public void RemoveRegistrationTicket()
        {
            var ticket = DatabaseCoreFactory.CreateTicket();

            _databaseCore.WriteRegistrationTicket(ticket);
            _databaseCore.RemoveRegistrationTicket(ticket.Id);

            Assert.Null(_databaseCore.ReadRegistrationTicket(ticket.Id));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralRemoveRegistrationTickets")]
        public void SeveralRemoveRegistrationTickets(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var ticket = DatabaseCoreFactory.CreateTicket();

                _databaseCore.WriteRegistrationTicket(ticket);
                _databaseCore.RemoveRegistrationTicket(ticket.Id);

                Assert.Null(_databaseCore.ReadRegistrationTicket(ticket.Id));
            }
        }

        #endregion

        #region [ Configuration Object ]

        [Fact]
        [Trait("Category", "AddConfigurationObject")]
        public void AddConfigurationObjectTest()
        {
            var name = RandomGenerator.GetRandomString();
            var config = DatabaseCoreFactory.CreateConfiguration(name);

            _databaseCore.WriteConfigurationObject(config);

            FullConfigurationObjectTest(config, _databaseCore.GetConfigurationObject(name));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralConfigurationObject")]
        public void SeveralConfigurationObjectTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var name = RandomGenerator.GetRandomString();
                var config = DatabaseCoreFactory.CreateConfiguration(name);

                _databaseCore.WriteConfigurationObject(config);

                FullConfigurationObjectTest(config, _databaseCore.GetConfigurationObject(name));
            }
        }

        [Fact]
        [Trait("Category", "UpdateConfigurationObject")]
        public void UpdateConfigurationObjectTest()
        {
            var name = RandomGenerator.GetRandomString();
            var config = DatabaseCoreFactory.CreateConfiguration(name);

            _databaseCore.WriteConfigurationObject(config);
            config.Value = RandomGenerator.GetRandomString();
            _databaseCore.WriteConfigurationObject(config);

            FullConfigurationObjectTest(config, _databaseCore.GetConfigurationObject(name));
        }

        [Fact]
        [Trait("Category", "RemoveConfigurationObject")]
        public void RemoveConfigurationObject()
        {
            var name = RandomGenerator.GetRandomString();
            var config = DatabaseCoreFactory.CreateConfiguration(name);

            _databaseCore.WriteConfigurationObject(config);
            _databaseCore.RemoveConfigurationObject(name);

            Assert.Null(_databaseCore.GetConfigurationObject(name));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "SeveralRemoveConfigurationObject")]
        public void SeveralRemoveConfigurationObject(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var name = RandomGenerator.GetRandomString();
                var config = DatabaseCoreFactory.CreateConfiguration(name);

                _databaseCore.WriteConfigurationObject(config);
                _databaseCore.RemoveConfigurationObject(name);

                Assert.Null(_databaseCore.GetConfigurationObject(name));
            }
        }

        #endregion

        #region [ Private methods ]

        private static void FullProductTest(ProductEntity expectedProduct, ProductEntity actualProduct)
        {
            Assert.NotNull(actualProduct);
            Assert.Equal(expectedProduct.Id, actualProduct.Id);
            Assert.Equal(expectedProduct.AuthorId, actualProduct.AuthorId);
            Assert.Equal(expectedProduct.ParentProductId, actualProduct.ParentProductId);
            Assert.Equal(expectedProduct.DisplayName, actualProduct.DisplayName);
            Assert.Equal(expectedProduct.State, actualProduct.State);
            Assert.Equal(expectedProduct.Description, actualProduct.Description);
            Assert.Equal(expectedProduct.CreationDate, actualProduct.CreationDate);
            Assert.Equal(expectedProduct.SubProductsIds, actualProduct.SubProductsIds);
            Assert.Equal(expectedProduct.SensorsIds, actualProduct.SensorsIds);
        }

        private static void FullKeyTest(AccessKeyEntity expected, AccessKeyEntity actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.AuthorId, actual.AuthorId);
            Assert.Equal(expected.ProductId, actual.ProductId);
            Assert.Equal(expected.State, actual.State);
            Assert.Equal(expected.Permissions, actual.Permissions);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.CreationTime, actual.CreationTime);
            Assert.Equal(expected.ExpirationTime, actual.ExpirationTime);
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
                expectedUser.ProductsRoles = expectedUser.ProductsRoles.OrderBy(pr => pr.Key).ToList();
                actualUser.ProductsRoles = actualUser.ProductsRoles.OrderBy(pr => pr.Key).ToList();

                for (int i = 0; i < expectedUser.ProductsRoles.Count; i++)
                {
                    var expectedRole = expectedUser.ProductsRoles[i];
                    var actualRole = actualUser.ProductsRoles[i];

                    Assert.Equal(expectedRole.Key, actualRole.Key);
                    Assert.Equal(expectedRole.Value, actualRole.Value);
                }
            }
        }

        private static void FullTicketTest(RegistrationTicket expectedTicket, RegistrationTicket actualTicket)
        {
            Assert.NotNull(actualTicket);
            Assert.Equal(expectedTicket.Id, actualTicket.Id);
            Assert.Equal(expectedTicket.Role, actualTicket.Role);
            Assert.Equal(expectedTicket.ProductKey, actualTicket.ProductKey);
            Assert.Equal(expectedTicket.ExpirationDate, actualTicket.ExpirationDate);
        }

        private static void FullConfigurationObjectTest(ConfigurationObject expectedConfig, ConfigurationObject actualConfig)
        {
            Assert.NotNull(actualConfig);
            Assert.Equal(expectedConfig.Name, expectedConfig.Name);
            //Entity doesn't have this field
            //Assert.Equal(expectedConfig.Description, actualConfig.Description);
            Assert.Equal(expectedConfig.Value, actualConfig.Value);
        }

        private User GetUser(string username) =>
            _databaseCore.GetUsers().FirstOrDefault(u => u.UserName.Equals(username));

        private static int GetCountItemsOnPage(int count, int pageNumber, int pageSize)
        {
            pageNumber--;

            if (pageSize == 0) return 0;

            if (pageNumber == count / pageSize)
                return count % pageSize;
            else if (pageNumber > count / pageSize)
                return 0;
            else if (pageNumber < count / pageSize)
                return pageSize;

            return -1;
        }

        private static AccessKeyEntity AddKey(AddAccessKey addKey)
        {
            var key = DatabaseCoreFactory.CreateAccessKey();
            addKey?.Invoke(key);

            return key;
        }

        #endregion
    }
}
