using HSMServer.Tests.Fixture;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using Xunit;
using Assert = Xunit.Assert;

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
            Assert.Equal(product.DisplayName, existingProduct.DisplayName);
            Assert.Equal(product.Id, existingProduct.Id);
            Assert.Equal(product.CreationDate, existingProduct.CreationDate);
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
            _databaseFixture.DatabaseAdapter.RemoveProduct(product.DisplayName);
            var correspondingProduct = _databaseFixture.DatabaseAdapter.GetProduct(product.DisplayName);

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
            Assert.Contains(list, p => p.DisplayName == product.DisplayName && p.Id == product.Id);
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
        public void UsersPageMustBeRead()
        {
            //Arrange
            var user1 = _databaseFixture.CreateFirstUser();
            var user2 = _databaseFixture.CreateSecondUser();
            var user3 = _databaseFixture.CreateThirdUser();

            //Act
            _databaseFixture.DatabaseAdapter.AddUser(user1);
            _databaseFixture.DatabaseAdapter.AddUser(user2);
            _databaseFixture.DatabaseAdapter.AddUser(user3);
            var page = _databaseFixture.DatabaseAdapter.GetUsersPage(2, 1);

            //Assert
            Assert.NotNull(page);
            Assert.Equal(1, page.Count);
        }

        [Fact]
        public void UsersEmptyPageMustBeReturned()
        {
            //Arrange
            var user1 = _databaseFixture.CreateFirstUser();
            var user2 = _databaseFixture.CreateSecondUser();
            var user3 = _databaseFixture.CreateThirdUser();

            //Act
            _databaseFixture.DatabaseAdapter.AddUser(user1);
            _databaseFixture.DatabaseAdapter.AddUser(user2);
            _databaseFixture.DatabaseAdapter.AddUser(user3);
            var page = _databaseFixture.DatabaseAdapter.GetUsersPage(3, 5);

            //Assert
            Assert.NotNull(page);
            Assert.Empty(page);
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
            var key = _databaseFixture.GetFirstTestProduct().Id;
            user.ProductsRoles.Add(new KeyValuePair<string, ProductRoleEnum>(key, ProductRoleEnum.ProductManager));
            existingUser.Update(user);
            Thread.Sleep(1000);
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
            var infoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(product.DisplayName, info.Path);

            //Assert
            Assert.NotNull(infoFromDB);
            Assert.Equal(info.SensorName, infoFromDB.SensorName);
            Assert.Equal(info.Description, infoFromDB.Description);
            Assert.Equal(info.Path, infoFromDB.Path);
            Assert.Equal(info.ProductName, infoFromDB.ProductName);
        }

        [Fact]
        public async Task SensorMustBeRemoved()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateSensorInfo();
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            _databaseFixture.DatabaseAdapter.AddSensor(info);

            //Act
            _databaseFixture.DatabaseAdapter.RemoveSensor(product.DisplayName, info.Path);

            await Task.Delay(100);

            var infoFromEnvironmentDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(product.DisplayName, info.Path);
            var sensorFromMonitoringDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(product.DisplayName, info.Path);

            //Assert
            Assert.NotNull(infoFromEnvironmentDB);
            Assert.Null(sensorFromMonitoringDB);
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
            _databaseFixture.DatabaseAdapter.PutSensorData(data, product.DisplayName);
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(product.DisplayName, info.Path);

            //Assert
            Assert.NotNull(dataFromDB);
            Assert.Equal(data.DataType, (byte)dataFromDB.SensorType);
            Assert.Equal(data.TypedData, dataFromDB.TypedData);
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
            _databaseFixture.DatabaseAdapter.PutSensorData(data, product.DisplayName);
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(product.DisplayName, info.Path);

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
            data.ForEach(d => _databaseFixture.DatabaseAdapter.PutSensorData(d, product.DisplayName));
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetAllSensorHistory(product.DisplayName, info.Path);

            //Assert
            Assert.NotEmpty(dataFromDB);
            Assert.Equal(data.Count, dataFromDB.Count);
        }

        [Fact]
        public void AllSensorValuesFromDifferentThreadsMustBeAdded()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateSensorInfo();
            var data = _databaseFixture.CreateSensorValues();
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            _databaseFixture.DatabaseAdapter.AddSensor(info);

            //Act
            data.ForEach(d => Task.Run(() => _databaseFixture.DatabaseAdapter.PutSensorData(d, product.DisplayName)));
            Thread.Sleep(3000);
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetAllSensorHistory(product.DisplayName, info.Path);

            //Assert
            Assert.NotEmpty(dataFromDB);
            Assert.Equal(data.Count, dataFromDB.Count);
        }


        [Fact]
        public void SensorValuesAfterSpecifiedDateMustBeReturned()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateSensorInfo2();
            var data = _databaseFixture.CreateSensorValues2();
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            _databaseFixture.DatabaseAdapter.AddSensor(info);

            //Act
            data.ForEach(d => _databaseFixture.DatabaseAdapter.PutSensorData(d, product.DisplayName));
            DateTime dateTime = DateTime.Now.AddDays(-1 * 9);
            var expectedData = data.Where(e => e.TimeCollected > dateTime).ToList();
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistory(product.DisplayName, info.Path, dateTime);

            //Assert
            Assert.NotEmpty(dataFromDB);
            Assert.Equal(expectedData.Count, dataFromDB.Count);
            expectedData.Sort((d1, d2) => d2.TimeCollected.CompareTo(d1.TimeCollected));
            dataFromDB.Sort((d1, d2) => d2.Time.CompareTo(d1.Time));
            for (int i = 0; i < expectedData.Count; ++i)
            {
                Assert.Equal(expectedData[i].TypedData, dataFromDB[i].TypedData);
                Assert.Equal(expectedData[i].DataType, (byte)dataFromDB[i].SensorType);
            }
        }

        [Fact]
        public void SensorValuesFromTheGivenPeriodMustBeReturned()
        {
            //Arrange
            var product = _databaseFixture.GetFirstTestProduct();
            var info = _databaseFixture.CreateSensorInfo();
            var data = _databaseFixture.CreateSensorValues();
            _databaseFixture.DatabaseAdapter.AddProduct(product);
            _databaseFixture.DatabaseAdapter.AddSensor(info);

            //Act
            data.ForEach(d => _databaseFixture.DatabaseAdapter.PutSensorData(d, product.DisplayName));
            DateTime from = DateTime.Now.AddDays(-1 * 15);
            DateTime to = DateTime.Now.AddDays(-1 * 4);
            var expectedData = data.Where(e => e.TimeCollected < to
                && e.TimeCollected > from).ToList();
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistory(product.DisplayName, info.Path, from, to);

            //Assert
            Assert.NotEmpty(dataFromDB);
            Assert.Equal(expectedData.Count, dataFromDB.Count);
            expectedData.Sort((d1, d2) => d2.TimeCollected.CompareTo(d1.TimeCollected));
            dataFromDB.Sort((d1, d2) => d2.Time.CompareTo(d1.Time));
            for (int i = 0; i < expectedData.Count; ++i)
            {
                Assert.Equal(expectedData[i].TypedData, dataFromDB[i].TypedData);
                Assert.Equal(expectedData[i].DataType, (byte)dataFromDB[i].SensorType);
            }
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
            data.ForEach(d => _databaseFixture.DatabaseAdapter.PutSensorData(d, product.DisplayName));
            Thread.Sleep(5000);
            _databaseFixture.DatabaseAdapter.RemoveProduct(product.DisplayName);
            var dataFromDB = _databaseFixture.DatabaseAdapter.GetSensorHistory(product.DisplayName, info.Path, DateTime.MinValue);

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
            _databaseFixture.DatabaseAdapter.WriteRegistrationTicket(ticket);
            var ticketFromDB = _databaseFixture.DatabaseAdapter.ReadRegistrationTicket(ticket.Id);

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
            _databaseFixture.DatabaseAdapter.WriteRegistrationTicket(ticket);
            Thread.Sleep(1000);
            _databaseFixture.DatabaseAdapter.RemoveRegistrationTicket(ticket.Id);
            var ticketFromDB = _databaseFixture.DatabaseAdapter.ReadRegistrationTicket(ticket.Id);

            //Assert
            Assert.Null(ticketFromDB);
        }

        [Fact]
        public void ConfigurationObjectMustBeAdded()
        {
            //Arrange
            var configObj = _databaseFixture.CreateConfigurationObject();

            //Act
            _databaseFixture.DatabaseAdapter.WriteConfigurationObject(configObj);
            var objectFromDB = _databaseFixture.DatabaseAdapter.GetConfigurationObject(configObj.Name);

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
            _databaseFixture.DatabaseAdapter.WriteConfigurationObject(configObj);
            string newValue = "New value";
            configObj.Value = newValue;
            _databaseFixture.DatabaseAdapter.WriteConfigurationObject(configObj);
            var objectFromDB = _databaseFixture.DatabaseAdapter.GetConfigurationObject(configObj.Name);

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
            _databaseFixture.DatabaseAdapter.WriteConfigurationObject(configObj);
            _databaseFixture.DatabaseAdapter.RemoveConfigurationObject(configObj.Name);
            var objectFromDB = _databaseFixture.DatabaseAdapter.GetConfigurationObject(configObj.Name);

            //Assert
            Assert.Null(objectFromDB);
        }

        public void Dispose()
        {
            _databaseFixture?.Dispose();
        }
    }
}
