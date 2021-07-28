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

        #region Sensors

        [Fact]
        public void SensorMustBeAdded()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateSensorInfo();
            _databaseFixture.DatabaseAdapter.AddProduct(product);


            //Act
            _databaseFixture.DatabaseAdapter.AddSensor(info);
            var infoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(product.Name, info.Path);

            //Assert
            Assert.NotNull(infoFromDB);
            Assert.Equal(info.SensorName, infoFromDB.SensorName);
            Assert.Equal(info.Description, infoFromDB.Description);
            Assert.Equal(info.Path, infoFromDB.Path);
            Assert.Equal(info.ProductName, infoFromDB.ProductName);
        }

        [Fact]
        public void SensorMustBeRemoved()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateSensorInfo();
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            _databaseFixture.DatabaseAdapter.AddSensor(info);

            //Act
            _databaseFixture.DatabaseAdapter.RemoveSensor(product.Name, info.Path);
            var infoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(product.Name, info.Path);

            //Assert
            Assert.Null(infoFromDB);
        }

        [Fact]
        public void SensorValueMustBeAdded()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateSensorInfo();
            var data = _databaseFixture.CreateOneDataEntity();
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            _databaseFixture.DatabaseAdapter.AddSensor(info);

            //Act
            _databaseFixture.DatabaseAdapter.PutSensorData(data, product.Name);
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistory(product.Name, info.Path, -1);

            //Assert
            Assert.NotEmpty(dataFromDB);
            Assert.Equal(data.DataType, (byte)dataFromDB[0].SensorType);
            Assert.Equal(data.TypedData, dataFromDB[0].TypedData);
        }

        [Fact]
        public void OneValueSensorValueMustBeAdded()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateOneValueSensorInfo();
            var data = _databaseFixture.CreateOneValueSensorDataEntity();
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            _databaseFixture.DatabaseAdapter.AddSensor(info);

            //Act
            _databaseFixture.DatabaseAdapter.PutOneValueSensorData(data, product.Name);
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(product.Name, info.Path);

            //Assert
            Assert.NotNull(dataFromDB);
            Assert.Equal(data.DataType, (byte)dataFromDB.SensorType);
            Assert.Equal(data.TypedData, dataFromDB.TypedData);
        }

        [Fact]
        public void AllSensorValuesMustBeAdded()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateSensorInfo();
            var data = _databaseFixture.CreateSensorValues();
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            _databaseFixture.DatabaseAdapter.AddSensor(info);

            //Act
            data.ForEach(d => _databaseFixture.DatabaseAdapter.PutSensorData(d, product.Name));
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistory(product.Name, info.Path, -1);

            //Assert
            Assert.NotEmpty(dataFromDB);
            Assert.Equal(data.Count, dataFromDB.Count);
        }

        [Fact]
        public void RequestedSensorValuesMustBeReturned()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateSensorInfo();
            var data = _databaseFixture.CreateSensorValues();
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            _databaseFixture.DatabaseAdapter.AddSensor(info);

            //Act
            data.ForEach(d => _databaseFixture.DatabaseAdapter.PutSensorData(d, product.Name));
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistory(product.Name, info.Path, 10);

            //Assert
            Assert.NotEmpty(dataFromDB);
            Assert.Equal(10, dataFromDB.Count);
        }

        [Fact]
        public void SensorValuesMustBeRemovedWithProduct()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateSensorInfo();
            var data = _databaseFixture.CreateSensorValues();
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            _databaseFixture.DatabaseAdapter.AddSensor(info);

            //Act
            data.ForEach(d => _databaseFixture.DatabaseAdapter.PutSensorData(d, product.Name));
            _databaseFixture.DatabaseAdapter.RemoveProduct(product.Name);
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistory(product.Name, info.Path, -1);

            //Assert
            Assert.NotNull(dataFromDB);
            Assert.Empty(dataFromDB);
        }

        #endregion
        public void Dispose()
        {
            _databaseFixture?.Dispose();
        }
    }
}