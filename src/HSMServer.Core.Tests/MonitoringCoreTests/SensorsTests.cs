using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Authentication;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    public class SensorsTests : MonitoringCoreTestsBase<SensorsFixture>
    {
        private readonly string _testProductName = TestProductsManager.ProductName;

        private delegate void AddSensor(string productName, SensorValueBase sensorValue);
        private delegate bool IsSensorRegistered(string productName, string path);
        private delegate SensorInfo GetSensorInfo(string productName, string path);
        private delegate ICollection<SensorInfo> GetProductSensors(string productName);
        private delegate List<SensorData> GetQueueValues(List<string> products);
        private delegate SensorInfo GetSensorInfoFromDB(string productName, string path);
        private delegate List<SensorHistoryData> GetAllSensorHistory(string productName, string path);


        public SensorsTests(SensorsFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            var userManager = new Mock<IUserManager>();
            var barStorage = new BarSensorsStorage();// new Mock<IBarSensorsStorage>();
            var configurationProvider = new Mock<IConfigurationProvider>();

            var monitoringLogger = CommonMoqs.CreateNullLogger<MonitoringCore>();
            _monitoringCore = new MonitoringCore(
                _databaseAdapterManager.DatabaseAdapter,
                userManager.Object,
                barStorage,
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
                sensorValues.Add(AddAndGetRandomSensorValue(i.ToString()));

            _monitoringCore.RemoveSensors(_testProductName, TestProductsManager.TestProduct.Key, sensorValues.Select(s => s.Path));

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

            _monitoringCore.RemoveSensors(_testProductName, TestProductsManager.TestProduct.Key, new List<string>() { sensorValue.Path });

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


        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [Trait("Category", "Get Sensors History Data")]
        public void GetAllSensorsHistoryDataTest(int count)
        {
            var sensorValues = AddRandomSensorValuesAndGetTheirValues(count);

            foreach (var sensorValue in sensorValues)
            {
                var history = _monitoringCore.GetAllSensorHistory(_testProductName, sensorValue.Key);

                for (int i = 0; i < sensorValue.Value.Count; ++i)
                    SensorValuesTester.TestSensorHistoryDataFromDB(sensorValue.Value[i], history[i]);
            }
        }

        [Theory]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "Get Sensors History Data")]
        public void GetAllSensorsHistoryData_WithBarValues_Test(SensorType type)
        {
            const int expectedCount = 11;

            var barSensorValues = AddAndGetBarSensorValues(expectedCount, type);

            var history = _monitoringCore.GetAllSensorHistory(_testProductName, barSensorValues[0].Path);

            Assert.Equal(expectedCount, history.Count);
            Assert.DoesNotContain(JsonSerializer.Serialize(DateTime.MinValue), history[expectedCount - 1].TypedData);

            for (int i = 0; i < expectedCount - 1; ++i)
                SensorValuesTester.TestSensorHistoryDataFromDB(barSensorValues[i], history[i]);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [Trait("Category", "Get Sensors History Data")]
        public void GetSensorsHistoryDataFromToTest(int count)
        {
            DateTime from = DateTime.UtcNow;

            var sensorValues = AddRandomSensorValuesAndGetTheirValues(count);

            DateTime to = DateTime.UtcNow;

            foreach (var sensorValue in sensorValues)
            {
                var history = _monitoringCore.GetSensorHistory(_testProductName, sensorValue.Key, from, to);

                for (int i = 0; i < sensorValue.Value.Count; ++i)
                    SensorValuesTester.TestSensorHistoryDataFromDB(sensorValue.Value[i], history[i]);
            }
        }

        [Theory]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "Get Sensors History Data")]
        public void GetSensorsHistoryDataFromTo_WithBarValues_Test(SensorType type)
        {
            const int expectedCount = 11;

            DateTime from = DateTime.UtcNow;

            var barSensorValues = AddAndGetBarSensorValues(expectedCount, type);

            DateTime to = DateTime.UtcNow;

            var history = _monitoringCore.GetSensorHistory(_testProductName, barSensorValues[0].Path, from, to);

            Assert.Equal(expectedCount, history.Count);
            Assert.DoesNotContain(JsonSerializer.Serialize(DateTime.MinValue), history[expectedCount - 1].TypedData);

            for (int i = 0; i < expectedCount - 1; ++i)
                SensorValuesTester.TestSensorHistoryDataFromDB(barSensorValues[i], history[i]);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [Trait("Category", "Get Sensors History Data")]
        public void GetSensorsHistoryDataSpecificCountTest(int count)
        {
            const int specificCount = 5;

            var sensorValues = AddRandomSensorValuesAndGetTheirValues(count, specificCount);

            foreach (var sensorValue in sensorValues)
            {
                var history = _monitoringCore.GetSensorHistory(_testProductName, sensorValue.Key, specificCount);

                for (int i = 0; i < sensorValue.Value.Count; ++i)
                    SensorValuesTester.TestSensorHistoryDataFromDB(sensorValue.Value[i], history[i]);
            }
        }

        [Theory]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "Get Sensors History Data")]
        public void GetSensorsHistoryDataSpecificCount_WithBarValues_Test(SensorType type)
        {
            const int expectedCount = 101;
            const int specificCount = 5;

            var barSensorValues = AddAndGetBarSensorValues(expectedCount, type).TakeLast(specificCount).ToList();

            var history = _monitoringCore.GetSensorHistory(_testProductName, barSensorValues[0].Path, specificCount);

            Assert.Equal(specificCount, history.Count);
            Assert.DoesNotContain(JsonSerializer.Serialize(DateTime.MinValue), history[specificCount - 1].TypedData);

            for (int i = 0; i < specificCount - 1; ++i)
                SensorValuesTester.TestSensorHistoryDataFromDB(barSensorValues[i], history[i]);
        }


        [Fact]
        [Trait("Category", "Get FileSensor content")]
        public void GetFileSensorValueBytesTest()
        {
            var bytes = _monitoringCore.GetFileSensorValueBytes(RandomGenerator.GetRandomString(), RandomGenerator.GetRandomString());

            Assert.Empty(bytes);
        }

        [Theory(Skip = "TODO fix GetFileSensorValueBytes")]
        [InlineData(SensorType.FileSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "Get FileSensor content")]
        public void GetFileSensorContentTest(SensorType type)
        {
            var sensorValue = AddAndGetSensorValue(type);
            var expectedContent = sensorValue is FileSensorValue fileSensor
                ? Encoding.Default.GetBytes(fileSensor.FileContent)
                : (sensorValue as FileSensorBytesValue).FileContent;

            var actualContent = _monitoringCore.GetFileSensorValueBytes(TestProductsManager.ProductName, sensorValue.Path);

            Assert.Equal(expectedContent, actualContent);
        }

        [Fact]
        [Trait("Category", "Get FileSensor extension")]
        public void GetFileSensorValueExtensionTest()
        {
            var extension = _monitoringCore.GetFileSensorValueExtension(RandomGenerator.GetRandomString(), RandomGenerator.GetRandomString());

            Assert.Empty(extension);
        }

        [Theory]
        [InlineData(SensorType.FileSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "Get FileSensor extension")]
        public void GetFileSensorExtensionTest(SensorType type)
        {
            var sensorValue = AddAndGetSensorValue(type);
            var expectedExtension = sensorValue is FileSensorValue fileSensor
                ? fileSensor.Extension
                : (sensorValue as FileSensorBytesValue).Extension;

            var eactualEtension = _monitoringCore.GetFileSensorValueExtension(TestProductsManager.ProductName, sensorValue.Path);

            Assert.Equal(expectedExtension, eactualEtension);
        }


        [Fact]
        [Trait("Category", "Get sensors data")]
        public void GetSensorUpdates()
        {
            // initialize queueManager
            _monitoringCore.GetSensorsTree(TestUsersManager.TestUser);

            var sensorValue = AddAndGetRandomSensorValue();
            _monitoringCore.RemoveSensors(TestProductsManager.ProductName, sensorValue.Key, new List<string>() { sensorValue.Path });

            var result = _monitoringCore.GetSensorUpdates(TestUsersManager.TestUser);

            Assert.Equal(2, result.Count);
            _sensorValuesTester.TestSensorDataFromCache(sensorValue, result[0]);
            TestRemovedSensorData(sensorValue, TestProductsManager.ProductName, result[1]);
        }

        [Fact]
        [Trait("Category", "Get sensors data")]
        public void GetSensorsTreeTest()
        {
            var sensorsLastValues = AddRandomSensorValuesAndGetTheirLastValues(10);

            var result = _monitoringCore.GetSensorsTree(TestUsersManager.TestUser);

            Assert.Equal(sensorsLastValues.Count, result.Count);
            foreach (var sensorData in result)
                _sensorValuesTester.TestSensorDataFromCache(sensorsLastValues[sensorData.Path], sensorData);
        }


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

        private static void TestRemovedSensorData(SensorValueBase expected, string expectedProductName, SensorData actual)
        {
            Assert.Equal(expectedProductName, actual.Product);
            Assert.Equal(expected.Key, actual.Key);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(TransactionType.Delete, actual.TransactionType);
            Assert.True(DateTime.UtcNow > actual.Time);
        }


        private SensorValueBase AddAndGetSensorValue(SensorType type)
        {
            var sensorValue = _sensorValuesFactory.BuildSensorValue(type);

            _monitoringCore.AddSensorValue(sensorValue);

            return sensorValue;
        }

        private SensorValueBase AddAndGetRandomSensor(AddSensor addSensor = null, string productName = null)
        {
            var sensorValue = _sensorValuesFactory.BuildRandomSensorValue();

            (addSensor ?? _monitoringCore.AddSensor)?.Invoke(productName ?? _testProductName, sensorValue);

            return sensorValue;
        }

        private SensorValueBase AddAndGetRandomSensorValue(string specificPathPart = null)
        {
            var sensorValue = _sensorValuesFactory.BuildRandomSensorValue();

            if (!string.IsNullOrEmpty(specificPathPart))
                sensorValue.Path = $"{sensorValue.Path}{specificPathPart}";

            _monitoringCore.AddSensorValue(sensorValue);

            return sensorValue;
        }

        private Dictionary<string, SensorValueBase> AddRandomSensorValuesAndGetTheirLastValues(int count)
        {
            var sensorsLastValues = new Dictionary<string, SensorValueBase>();
            for (int i = 0; i < count; ++i)
            {
                var sensorValue = AddAndGetRandomSensorValue();
                sensorsLastValues[sensorValue.Path] = sensorValue;
            }

            return sensorsLastValues;
        }

        private Dictionary<string, List<SensorValueBase>> AddRandomSensorValuesAndGetTheirValues(int countToAdd, int? specificCount = null)
        {
            if (!specificCount.HasValue)
                specificCount = countToAdd;

            var sensorValues = new List<SensorValueBase>();

            for (int i = 0; i < countToAdd; ++i)
                sensorValues.Add(AddAndGetRandomSensorValue());

            return sensorValues.GroupBy(s => s.Path)
                               .ToDictionary(s => s.Key, s => s.TakeLast(specificCount.Value).ToList());
        }

        private List<SensorValueBase> AddAndGetBarSensorValues(int count, SensorType type)
        {
            var barSensorValues = new List<SensorValueBase>(count);

            for (int i = 0; i < count - 1; ++i)
            {
                var unaccountedSensorValueWithMinEndTime = _sensorValuesFactory.BuildUnitedSensorValue(type, true);
                _monitoringCore.AddSensorValue(unaccountedSensorValueWithMinEndTime);

                var sensorValue = _sensorValuesFactory.BuildUnitedSensorValue(type);
                _monitoringCore.AddSensorValue(sensorValue);
                barSensorValues.Add(sensorValue);
            }

            var sensorValueWithMinEndTime = _sensorValuesFactory.BuildUnitedSensorValue(type, true);
            _monitoringCore.AddSensorValue(sensorValueWithMinEndTime);
            barSensorValues.Add(sensorValueWithMinEndTime);

            return barSensorValues;
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
