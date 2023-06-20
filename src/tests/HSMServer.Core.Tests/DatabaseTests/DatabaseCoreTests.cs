﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.DatabaseTests.Fixture;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Model.Authentication;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Extensions;
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
            var product = EntitiesFactory.BuildProductEntity();
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
                var product = EntitiesFactory.BuildProductEntity();
                _databaseCore.AddProduct(product);

                FullProductTest(product, _databaseCore.GetProduct(product.Id));
            }
        }

        [Fact]
        [Trait("Category", "OneProductRemove")]
        public void RemoveProductTest()
        {
            var product = EntitiesFactory.BuildProductEntity();

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
                var product = EntitiesFactory.BuildProductEntity();

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

                FullKeyTest(key, _databaseCore.GetAccessKey(key.Id.ToGuid()));
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
                var id = key.Id.ToGuid();

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

                var updated = EntitiesFactory.BuildAccessKeyEntity(id: key.Id);
                _databaseCore.UpdateAccessKey(updated);

                FullKeyTest(updated, _databaseCore.GetAccessKey(key.Id.ToGuid()));
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
            var user = EntitiesFactory.BuildUser();
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
                var user = EntitiesFactory.BuildUser();
                _databaseCore.AddUser(user);

                var actualUser = GetUser(user.UserName);

                FullUserTest(user, actualUser);
            }
        }

        [Fact]
        [Trait("Category", "OneUserRemove")]
        public void RemoveUserTest()
        {
            var user = EntitiesFactory.BuildUser();

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
                var user = EntitiesFactory.BuildUser();

                _databaseCore.AddUser(user);
                _databaseCore.RemoveUser(user);

                Assert.Null(GetUser(user.UserName));
            }
        }

        [Fact]
        [Trait("Category", "OneProductRole")]
        public void AddProductRoleTest()
        {
            var user = EntitiesFactory.BuildUser();
            var product = EntitiesFactory.BuildProductEntity();

            _databaseCore.AddUser(user);
            user.ProductsRoles.Add(new KeyValuePair<string, byte>(product.Id, (byte)ProductRoleEnum.ProductManager));
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
            var user = EntitiesFactory.BuildUser();
            _databaseCore.AddUser(user);

            for (int i = 0; i < count; i++)
            {
                var product = EntitiesFactory.BuildProductEntity();

                var role = i % 2 == 0 ? ProductRoleEnum.ProductManager : ProductRoleEnum.ProductViewer;
                user.ProductsRoles.Add(new KeyValuePair<string, byte>(product.Id, (byte)role));
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
                var user = EntitiesFactory.BuildUser();

                _databaseCore.AddUser(user);
            }

            var actualUsers = _databaseCore.GetUsersPage(page, pageSize);

            Assert.NotNull(actualUsers);
            Assert.Equal(GetCountItemsOnPage(count, page, pageSize), actualUsers.Count);
        }

        #endregion

        #region [ Folder Tests ]

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "AddFolder(s)")]
        public void AddFoldersTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var folder = EntitiesFactory.BuildFolderEntity();
                _databaseCore.AddFolder(folder);

                FullFolderTest(folder, _databaseCore.GetFolder(folder.Id));
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
        [Trait("Category", "RemoveFolder(s)")]
        public void RemoveFoldersTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var folder = EntitiesFactory.BuildFolderEntity();

                _databaseCore.AddFolder(folder);
                _databaseCore.RemoveFolder(folder.Id);

                Assert.Null(_databaseCore.GetFolder(folder.Id));
            }

            Assert.Empty(_databaseCore.GetAllFolders());
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

        private static void FullUserTest(UserEntity expectedUser, UserEntity actualUser)
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

        private static void FullFolderTest(FolderEntity expectedFolder, FolderEntity actualFolder)
        {
            Assert.NotNull(actualFolder);
            Assert.Equal(expectedFolder.Id, actualFolder.Id);
            Assert.Equal(expectedFolder.AuthorId, actualFolder.AuthorId);
            Assert.Equal(expectedFolder.DisplayName, actualFolder.DisplayName);
            Assert.Equal(expectedFolder.Description, actualFolder.Description);
            Assert.Equal(expectedFolder.CreationDate, actualFolder.CreationDate);
            Assert.Equal(expectedFolder.Color, actualFolder.Color);
        }

        private UserEntity GetUser(string username) =>
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
            var key = EntitiesFactory.BuildAccessKeyEntity();
            addKey?.Invoke(key);

            return key;
        }

        #endregion
    }
}
