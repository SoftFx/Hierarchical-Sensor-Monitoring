﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    public class SensorsTests : BaseFixture<SensorsFixture>
    {
        private readonly string _testProductName = DatabaseAdapterManager.ProductName;

        private delegate void AddSensor(string productName, SensorValueBase sensorValue);
        private delegate bool IsSensorRegistered(string productName, string path);
        private delegate SensorInfo GetSensorInfo(string productName, string path);
        private delegate ICollection<SensorInfo> GetProductSensors(string productName);
        private delegate List<SensorData> GetQueueValues(List<string> products);
        private delegate SensorInfo GetSensorInfoFromDB(string productName, string path);
        private delegate List<SensorHistoryData> GetAllSensorHistory(string productName, string path);


        public SensorsTests(SensorsFixture fixture) : base(fixture)
        {
            var userManager = new Mock<IUserManager>();
            var barStorage = new Mock<IBarSensorsStorage>();
            var configurationProvider = new Mock<IConfigurationProvider>();

            var monitoringLogger = CommonMoqs.CreateNullLogger<MonitoringCore>();
            _monitoringCore = new MonitoringCore(
                _databaseAdapterManager.DatabaseAdapter,
                userManager.Object,
                barStorage.Object,
                _productManager,
                configurationProvider.Object,
                _valuesCache,
                monitoringLogger);
        }


        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [Trait("Category", "Add Sensor(s)")]
        public void AddSensorTest(int sensorsCount)
        {
            for (int i = 0; i < sensorsCount; ++i)
            {
                var sensorValue = AddAndGetRandomSensor(_monitoringCore.AddSensor);

                FullTestSensorInfo(_testProductName,
                                   sensorValue,
                                   _monitoringCore.IsSensorRegistered,
                                   _monitoringCore.GetSensorInfo,
                                   _monitoringCore.GetProductSensors,
                                   _databaseAdapterManager.DatabaseAdapter.GetSensorInfo,
                                   _sensorValuesTester);
            }
        }

        [Fact]
        [Trait("Category", "Add Sensor(s)")]
        public void AddSensor_NonExistingProduct_Test()
        {
            var sensorValue = AddAndGetRandomSensor(_monitoringCore.AddSensor, RandomGenerator.GetRandomString());

            FullTestNonExistingSensorInfo(_testProductName,
                                          sensorValue.Path,
                                          _monitoringCore.IsSensorRegistered,
                                          _monitoringCore.GetSensorInfo,
                                          _monitoringCore.GetProductSensors,
                                          _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [Trait("Category", "Update Sensor(s)")]
        public void UpdateSensorInfoTest(int count)
        {
            var sensorValue = AddAndGetRandomSensor();
            var sensorValuePath = sensorValue.Path;

            var sensorInfo = _monitoringCore.GetSensorInfo(_testProductName, sensorValuePath);

            for (int i = 0; i < count; ++i)
            {
                var updatedSensorInfo = GetUpdatedSensorInfo(sensorInfo, i);

                _monitoringCore.UpdateSensorInfo(updatedSensorInfo);

                Assert.True(_monitoringCore.IsSensorRegistered(_testProductName, sensorValuePath));
                FullTestUpdatedSensorInfo(updatedSensorInfo, _databaseAdapterManager.DatabaseAdapter.GetSensorInfo(_testProductName, sensorValuePath), sensorValue);
                FullTestUpdatedSensorInfo(updatedSensorInfo, _monitoringCore.GetSensorInfo(_testProductName, sensorValuePath), sensorValue);
                FullTestUpdatedSensorInfo(updatedSensorInfo, _monitoringCore.GetProductSensors(_testProductName).FirstOrDefault(s => s.Path == sensorValuePath), sensorValue);
            }
        }

        [Fact]
        [Trait("Category", "Update Sensor(s)")]
        public void UpdateSensorInfo_NonExistingProduct_Test()
        {
            var sensorInfo = SensorInfoFactory.BuildSensorInfo(_testProductName, RandomGenerator.GetRandomByte());

            _monitoringCore.UpdateSensorInfo(sensorInfo);

            FullTestNonExistingSensorInfo(_testProductName,
                                          sensorInfo.Path,
                                          _monitoringCore.IsSensorRegistered,
                                          _monitoringCore.GetSensorInfo,
                                          _monitoringCore.GetProductSensors,
                                          _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [Trait("Category", "Remove Sensor(s)")]
        public void RemoveSensorsTest(int count)
        {
            List<SensorValueBase> sensorValues = new(count);

            for (int i = 0; i < count; ++i)
            {
                var sensorValue = _sensorValuesFactory.BuildRandomSensorValue();
                sensorValue.Path = $"{sensorValue.Path}{i}";

                _monitoringCore.AddSensorValue(sensorValue);
                sensorValues.Add(sensorValue);
            }

            _monitoringCore.RemoveSensors(_testProductName, _databaseAdapterManager.TestProduct.Key, sensorValues.Select(s => s.Path));

            foreach (var sensorValue in sensorValues)
                FullTestRemovedSensor(_testProductName,
                                      sensorValue,
                                      _monitoringCore.IsSensorRegistered,
                                      _monitoringCore.GetSensorInfo,
                                      _monitoringCore.GetProductSensors,
                                      _valuesCache.GetValues,
                                      _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                      _databaseAdapterManager.DatabaseAdapter.GetSensorInfo,
                                      _sensorValuesTester);
        }

        [Fact]
        [Trait("Category", "Remove Sensor(s)")]
        public void RemoveSensors_WithoutValues_Test()
        {
            var sensorValue = AddAndGetRandomSensor();

            _monitoringCore.RemoveSensors(_testProductName, _databaseAdapterManager.TestProduct.Key, new List<string>() { sensorValue.Path });

            FullTestRemovedSensor(_testProductName,
                                  sensorValue,
                                  _monitoringCore.IsSensorRegistered,
                                  _monitoringCore.GetSensorInfo,
                                  _monitoringCore.GetProductSensors,
                                  _valuesCache.GetValues,
                                  _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                  _databaseAdapterManager.DatabaseAdapter.GetSensorInfo,
                                  _sensorValuesTester);
        }

        [Fact]
        [Trait("Category", "Remove Sensor")]
        public void RemoveSensorTest()
        {
            var sensorValue = AddAndGetRandomSensor();

            _monitoringCore.RemoveSensor(_testProductName, sensorValue.Path);

            FullTestRemovedSensor(_testProductName,
                                  sensorValue,
                                  _monitoringCore.IsSensorRegistered,
                                  _monitoringCore.GetSensorInfo,
                                  _monitoringCore.GetProductSensors,
                                  _valuesCache.GetValues,
                                  _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                  _databaseAdapterManager.DatabaseAdapter.GetSensorInfo,
                                  _sensorValuesTester);
        }

        [Fact]
        [Trait("Category", "Remove Sensor")]
        public void RemoveSensor_NonExistingProduct_Test()
        {
            var sensorValue = AddAndGetRandomSensor();

            _monitoringCore.RemoveSensor(RandomGenerator.GetRandomString(), sensorValue.Path);

            FullTestSensorInfo(_testProductName,
                               sensorValue,
                               _monitoringCore.IsSensorRegistered,
                               _monitoringCore.GetSensorInfo,
                               _monitoringCore.GetProductSensors,
                               _databaseAdapterManager.DatabaseAdapter.GetSensorInfo,
                               _sensorValuesTester);
        }

        [Fact]
        [Trait("Category", "Is sensor registered")]
        public void IsSensorRegistered_NonExistingProduct_Test() =>
            Assert.False(_monitoringCore.IsSensorRegistered(RandomGenerator.GetRandomString(), RandomGenerator.GetRandomString()));

        [Fact]
        [Trait("Category", "Get sensor info")]
        public void GetSensorInfo_NonExistingProduct_Test() =>
            Assert.Null(_monitoringCore.GetSensorInfo(RandomGenerator.GetRandomString(), RandomGenerator.GetRandomString()));

        [Fact]
        [Trait("Category", "Get product sensors")]
        public void GetProductSensors_NonExistingProduct_Test() =>
            Assert.Null(_monitoringCore.GetProductSensors(RandomGenerator.GetRandomString()));


        private static void FullTestSensorInfo(string productName, SensorValueBase sensorValue, IsSensorRegistered isSensorRegistered,
            GetSensorInfo getSensorInfo, GetProductSensors getProductSensors, GetSensorInfoFromDB getSensorFromDB, SensorValuesTester tester)
        {
            var sensorValuePath = sensorValue.Path;

            Assert.True(isSensorRegistered(productName, sensorValuePath));
            tester.TestSensorInfoFromDB(sensorValue, getSensorInfo(productName, sensorValuePath));
            tester.TestSensorInfoFromDB(sensorValue, getProductSensors(productName).FirstOrDefault(s => s.Path == sensorValuePath));
            tester.TestSensorInfoFromDB(sensorValue, getSensorFromDB(productName, sensorValuePath));
        }

        private static void FullTestNonExistingSensorInfo(string productName, string sensorPath, IsSensorRegistered isSensorReqistered,
            GetSensorInfo getSensorInfo, GetProductSensors getProductSensors, GetSensorInfoFromDB getSensorInfoFromDB)
        {
            Assert.False(isSensorReqistered(productName, sensorPath));
            Assert.Null(getSensorInfo(productName, sensorPath));
            Assert.Null(getProductSensors(productName)?.FirstOrDefault(s => s.Path == sensorPath));
            Assert.Null(getSensorInfoFromDB(productName, sensorPath));
        }

        private static void FullTestUpdatedSensorInfo(SensorInfo expected, SensorInfo actual, SensorValueBase sensorValue)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.ProductName, actual.ProductName);
            Assert.Equal(expected.ExpectedUpdateInterval, actual.ExpectedUpdateInterval);
            Assert.Equal(expected.Unit, actual.Unit);
            Assert.Equal(sensorValue.Path, actual.SensorName);
            Assert.Equal(SensorValuesTester.GetSensorValueType(sensorValue), actual.SensorType);
            Assert.NotEqual(expected.SensorName, actual.SensorName);
            Assert.NotEqual(expected.SensorType, actual.SensorType);
            Assert.Empty(actual.ValidationParameters);
        }

        private static void FullTestRemovedSensor(string productName, SensorValueBase sensorValue,
            IsSensorRegistered isSensorRegistered, GetSensorInfo getSensorInfo, GetProductSensors getProductSensors,
            GetQueueValues getQueueValues, GetAllSensorHistory getAllSensorHistory,
            GetSensorInfoFromDB getSensorFromDB, SensorValuesTester tester)
        {
            string sensorValuePath = sensorValue.Path;

            Assert.False(isSensorRegistered(productName, sensorValuePath));
            Assert.Null(getSensorInfo(productName, sensorValuePath));
            Assert.Null(getProductSensors(productName).FirstOrDefault(s => s.Path == sensorValuePath));
            Assert.Empty(getQueueValues(new List<string>() { productName }));
            Assert.Empty(getAllSensorHistory(productName, sensorValuePath));
            tester.TestSensorInfoFromDB(sensorValue, getSensorFromDB(productName, sensorValuePath));
        }


        private SensorValueBase AddAndGetRandomSensor(AddSensor addSensor = null, string productName = null)
        {
            var sensorValue = _sensorValuesFactory.BuildRandomSensorValue();

            (addSensor ?? _monitoringCore.AddSensor)?.Invoke(productName ?? _testProductName, sensorValue);

            return sensorValue;
        }

        private static SensorInfo GetUpdatedSensorInfo(SensorInfo existingSensorInfo, int iteration)
        {
            string GetUpdatedString(string value, int iteration) => $"{value}-updated{iteration}";

            return new()
            {
                ProductName = existingSensorInfo.ProductName,
                Path = existingSensorInfo.Path,
                Description = GetUpdatedString(existingSensorInfo.Description, iteration),
                ExpectedUpdateInterval = TimeSpan.FromSeconds(100 + iteration),
                SensorName = GetUpdatedString(existingSensorInfo.SensorName, iteration),
                SensorType = GetAnotherRandomSensorType(existingSensorInfo.SensorType),
                Unit = GetUpdatedString(existingSensorInfo.Unit, iteration),
                ValidationParameters = new List<SensorValidationParameter>()
                {
                    new SensorValidationParameter(
                        new ValidationParameterEntity()
                        {
                            ValidationValue = GetUpdatedString(null, iteration),
                            ParameterType = RandomGenerator.GetRandomInt(min: 0, max: 6),
                        })
                },
            };
        }

        private static SensorType GetAnotherRandomSensorType(SensorType originalType) =>
            (SensorType)(((int)originalType + RandomGenerator.GetRandomInt(min: 1, max: 8)) % 8);
    }
}
