using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HSMServer.Authentication;
using HSMServer.DataLayer.Model;
using HSMServer.Keys;
using HSMServer.Tests.Fixture;
using Xunit;

namespace HSMServer.Tests.DatabaseTests
{
    public class DatabaseAdapterTests : IClassFixture<DatabaseAdapterFixture>, IDisposable
    {
        private readonly DatabaseAdapterFixture _databaseFixture;
        public DatabaseAdapterTests(DatabaseAdapterFixture databaseFixture)
        {
            _databaseFixture = databaseFixture;
        }

        #region Product tests

        [Fact]
        public void ProductMustBeAdded()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();

            //Act
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            var existingProduct = _databaseFixture.DatabaseAdapter.GetProduct(_databaseFixture.FirstProductName);

            //Assert
            Assert.Equal(product.Name, existingProduct.Name);
            Assert.Equal(product.Key, existingProduct.Key);
            Assert.Equal(product.DateAdded, existingProduct.DateAdded);
        }

        [Fact]
        public void ProductMustBeRemoved()
        {
            //Arrange
            var product = _databaseFixture.GetSecondTestProduct();

            //Act
            _databaseFixture.DatabaseAdapter.AddProduct(product);

            //Assert
            Assert.NotNull(_databaseFixture.DatabaseAdapter.GetProduct(_databaseFixture.SecondProductName));

            //Act
            _databaseFixture.DatabaseAdapter.RemoveProduct(product.Name);
            var correspondingProduct = _databaseFixture.DatabaseAdapter.GetProduct(product.Name);

            //Assert
            Assert.Null(correspondingProduct);
        }

        [Fact]
        public void ListMustReturnAddedProduct()
        {
            //Arrange
            var product = _databaseFixture.GetThirdTestProduct();

            //Act
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            var list = _databaseFixture.GetProductsList();
            Debug.Print($"List of {list.Count} products received");

            //Assert
            Assert.Contains(list, p => p.Name == product.Name && p.Key == product.Key);
        }

        [Fact]
        public void ExtraKeyMustBeAdded()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var key = KeyGenerator.GenerateExtraProductKey(product.Name, _databaseFixture.ExtraKeyName);
            var extraKey = new ExtraProductKey {Key = key, Name = _databaseFixture.ExtraKeyName};
            product.ExtraKeys = new List<ExtraProductKey> { extraKey };

            //Act
            _databaseFixture.DatabaseAdapter.UpdateProduct(product);

            //Assert
            var gotProduct = _databaseFixture.DatabaseAdapter.GetProduct(product.Name);
            Assert.NotEmpty(gotProduct.ExtraKeys);
            var keyFromDB = gotProduct.ExtraKeys.First();
            Assert.Equal(extraKey.Name, keyFromDB.Name);
            Assert.Equal(extraKey.Key, keyFromDB.Key);
        }

        #endregion

        #region Users

        [Fact]
        public void UserMustBeAdded()
        {
            //Arrange
            var user = _databaseFixture.CreateFirstUser();

            //Act
            _databaseFixture.DatabaseAdapter.AddUser(user);
            var usersFromDB = _databaseFixture.DatabaseAdapter.GetUsers();

            //Assert
            Assert.Contains(usersFromDB, u => u.UserName == user.UserName && u.Id == user.Id);
        }

        [Fact]
        public void UserMustBeRemoved()
        {
            //Arrange
            var user = _databaseFixture.CreateSecondUser();

            //Act
            _databaseFixture.DatabaseAdapter.AddUser(user);
            _databaseFixture.DatabaseAdapter.RemoveUser(user);
            var usersFromDB = _databaseFixture.DatabaseAdapter.GetUsers();

            //Assert
            Assert.DoesNotContain(usersFromDB, u => u.UserName == user.UserName && u.Id == user.Id);
        }

        [Fact]
        public void ProductRoleMustBeAdded()
        {
            //Arrange
            var user = _databaseFixture.CreateThirdUser();

            //Act
            _databaseFixture.DatabaseAdapter.AddUser(user);
            var existingUser = _databaseFixture.DatabaseAdapter.GetUsers().First(u => u.Id == user.Id);
            var key = _databaseFixture.GetFirstTestProduct().Key;
            user.ProductsRoles.Add(new KeyValuePair<string, ProductRoleEnum>(key, ProductRoleEnum.ProductManager));
            existingUser.Update(user);
            _databaseFixture.DatabaseAdapter.UpdateUser(existingUser);
            var newUser = _databaseFixture.DatabaseAdapter.GetUsers().First(u => u.Id == user.Id);

            //Assert
            Assert.NotEmpty(newUser.ProductsRoles);
            Assert.Equal(key, newUser.ProductsRoles.First().Key);
        }
        #endregion
        public void Dispose()
        {
            _databaseFixture?.Dispose();
        }
    }
}