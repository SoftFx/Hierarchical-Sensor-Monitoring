using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HSMServer.Authentication;
using HSMServer.DataLayer.Model;
using HSMServer.Keys;
using HSMServer.Tests.Fixture;
using Xunit;

namespace HSMServer.Tests.DatabaseTests
{
    public class DatabaseAdapterOldTests : IClassFixture<DatabaseAdapterFixture>, IDisposable
    {
        private readonly DatabaseAdapterFixture _databaseFixture;
        public DatabaseAdapterOldTests(DatabaseAdapterFixture databaseFixture)
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
            _databaseFixture.DatabaseAdapter.AddProductOld(product);
            var existingProduct = _databaseFixture.DatabaseAdapter.GetProductOld(_databaseFixture.FirstProductName);

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
            _databaseFixture.DatabaseAdapter.AddProductOld(product);

            //Assert
            Assert.NotNull(_databaseFixture.DatabaseAdapter.GetProductOld(_databaseFixture.SecondProductName));

            //Act
            _databaseFixture.DatabaseAdapter.RemoveProductOld(product.Name);
            var correspondingProduct = _databaseFixture.DatabaseAdapter.GetProductOld(product.Name);

            //Assert
            Assert.Null(correspondingProduct);
        }

        [Fact]
        public void ListMustReturnAddedProduct()
        {
            //Arrange
            var product = _databaseFixture.GetThirdTestProduct();

            //Act
            _databaseFixture.DatabaseAdapter.AddProductOld(product);
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
            _databaseFixture.DatabaseAdapter.UpdateProductOld(product);

            //Assert
            var gotProduct = _databaseFixture.DatabaseAdapter.GetProductOld(product.Name);
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
            _databaseFixture.DatabaseAdapter.AddUserOld(user);
            var usersFromDB = _databaseFixture.DatabaseAdapter.GetUsersOld();

            //Assert
            Assert.Contains(usersFromDB, u => u.UserName == user.UserName && u.Id == user.Id);
        }

        [Fact]
        public void UserMustBeRemoved()
        {
            //Arrange
            var user = _databaseFixture.CreateSecondUser();

            //Act
            _databaseFixture.DatabaseAdapter.AddUserOld(user);
            _databaseFixture.DatabaseAdapter.RemoveUserOld(user);
            var usersFromDB = _databaseFixture.DatabaseAdapter.GetUsersOld();

            //Assert
            Assert.DoesNotContain(usersFromDB, u => u.UserName == user.UserName && u.Id == user.Id);
        }

        [Fact]
        public void ProductRoleMustBeAdded()
        {
            //Arrange
            var user = _databaseFixture.CreateThirdUser();

            //Act
            _databaseFixture.DatabaseAdapter.AddUserOld(user);
            var existingUser = _databaseFixture.DatabaseAdapter.GetUsersOld().First(u => u.Id == user.Id);
            var key = _databaseFixture.GetFirstTestProduct().Key;
            user.ProductsRoles.Add(new KeyValuePair<string, ProductRoleEnum>(key, ProductRoleEnum.ProductManager));
            existingUser.Update(user);
            _databaseFixture.DatabaseAdapter.UpdateUserOld(existingUser);
            var newUser = _databaseFixture.DatabaseAdapter.GetUsersOld().First(u => u.Id == user.Id);

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
            _databaseFixture.DatabaseAdapter.AddProductOld(product);


            //Act
            _databaseFixture.DatabaseAdapter.AddSensorOld(info);
            var infoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfoOld(product.Name, info.Path);

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
            _databaseFixture.DatabaseAdapter.AddProductOld(product);
            _databaseFixture.DatabaseAdapter.AddSensorOld(info);

            //Act
            _databaseFixture.DatabaseAdapter.RemoveSensorOld(product.Name, info.Path);
            var infoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfoOld(product.Name, info.Path);

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
            _databaseFixture.DatabaseAdapter.AddProductOld(product);
            _databaseFixture.DatabaseAdapter.AddSensorOld(info);

            //Act
            _databaseFixture.DatabaseAdapter.PutSensorDataOld(data, product.Name);
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistoryOld(product.Name, info.Path, -1);

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
            _databaseFixture.DatabaseAdapter.AddProductOld(product);
            _databaseFixture.DatabaseAdapter.AddSensorOld(info);

            //Act
            _databaseFixture.DatabaseAdapter.PutOneValueSensorDataOld(data, product.Name);
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValueOld(product.Name, info.Path);

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
            _databaseFixture.DatabaseAdapter.AddProductOld(product);
            _databaseFixture.DatabaseAdapter.AddSensorOld(info);

            //Act
            data.ForEach(d => _databaseFixture.DatabaseAdapter.PutSensorDataOld(d, product.Name));
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistoryOld(product.Name, info.Path, -1);

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
            _databaseFixture.DatabaseAdapter.AddProductOld(product);
            _databaseFixture.DatabaseAdapter.AddSensorOld(info);

            //Act
            data.ForEach(d => _databaseFixture.DatabaseAdapter.PutSensorDataOld(d, product.Name));
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistoryOld(product.Name, info.Path, 10);

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
            _databaseFixture.DatabaseAdapter.AddProductOld(product);
            _databaseFixture.DatabaseAdapter.AddSensorOld(info);

            //Act
            data.ForEach(d => _databaseFixture.DatabaseAdapter.PutSensorDataOld(d, product.Name));
            _databaseFixture.DatabaseAdapter.RemoveProductOld(product.Name);
            Thread.Sleep(1000);
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistoryOld(product.Name, info.Path, -1);

            //Assert
            Assert.NotNull(dataFromDB);
            Assert.Empty(dataFromDB);
        }

        #endregion

        [Fact]
        public void RegisterTickerMustBeAdded()
        {
            //Arrange
            var ticket = _databaseFixture.CreateRegistrationTicket();

            //Act
            _databaseFixture.DatabaseAdapter.WriteRegistrationTicketOld(ticket);
            var ticketFromDB = _databaseFixture.DatabaseAdapter.ReadRegistrationTicketOld(ticket.Id);

            //Assert
            Assert.NotNull(ticketFromDB);
            Assert.Equal(ticket.Id, ticketFromDB.Id);
            Assert.Equal(ticket.Role, ticketFromDB.Role);
            Assert.Equal(ticket.ProductKey, ticketFromDB.ProductKey);
            Assert.Equal(ticket.ExpirationDate, ticketFromDB.ExpirationDate);
        }

        [Fact]
        public void RegisterTicketMustBeRemoved()
        {
            //Arrange
            var ticket = _databaseFixture.CreateRegistrationTicket();

            //Act
            _databaseFixture.DatabaseAdapter.WriteRegistrationTicketOld(ticket);
            _databaseFixture.DatabaseAdapter.RemoveRegistrationTicketOld(ticket.Id);
            var ticketFromDB = _databaseFixture.DatabaseAdapter.ReadRegistrationTicketOld(ticket.Id);

            //Assert
            Assert.Null(ticketFromDB);
        }

        [Fact]
        public void ConfigurationObjectMustBeAdded()
        {
            //Arrange
            var configObj = _databaseFixture.CreateConfigurationObject();

            //Act
            _databaseFixture.DatabaseAdapter.WriteConfigurationObjectOld(configObj);
            var objectFromDB = _databaseFixture.DatabaseAdapter.GetConfigurationObjectOld(configObj.Name);

            //Assert
            Assert.NotNull(objectFromDB);
            Assert.Equal(configObj.Name, objectFromDB.Name);
            Assert.Equal(configObj.Value, objectFromDB.Value);
        }

        [Fact]
        public void ConfigurationObjectMustBeUpdated()
        {
            //Arrange
            var configObj = _databaseFixture.CreateConfigurationObject();

            //Act
            _databaseFixture.DatabaseAdapter.WriteConfigurationObjectOld(configObj);
            string newValue = "New value";
            configObj.Value = newValue;
            _databaseFixture.DatabaseAdapter.WriteConfigurationObjectOld(configObj);
            var objectFromDB = _databaseFixture.DatabaseAdapter.GetConfigurationObjectOld(configObj.Name);

            //Assert
            Assert.NotNull(objectFromDB);
            Assert.Equal(newValue, objectFromDB.Value);
        }

        [Fact]
        public void ConfigurationObjectMustBeRemoved()
        {
            //Arrange
            var configObj = _databaseFixture.CreateConfigurationObject();

            //Act
            _databaseFixture.DatabaseAdapter.WriteConfigurationObjectOld(configObj);
            _databaseFixture.DatabaseAdapter.RemoveConfigurationObjectOld(configObj.Name);
            var objectFromDB = _databaseFixture.DatabaseAdapter.GetConfigurationObjectOld(configObj.Name);

            //Assert
            Assert.Null(objectFromDB);
        }

        public void Dispose()
        {
            _databaseFixture?.Dispose();
        }
    }
}