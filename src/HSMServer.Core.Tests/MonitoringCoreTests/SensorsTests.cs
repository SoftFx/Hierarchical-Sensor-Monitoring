using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
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
            var configurationProvider = new Mock<IConfigurationProvider>();

            var barStorage = new BarSensorsStorage();

            var monitoringLogger = CommonMoqs.CreateNullLogger<MonitoringCore>();
            _monitoringCore = new MonitoringCore(
                _databaseAdapterManager.DatabaseAdapter,
                userManager.Object,
                barStorage,
                _productManager,
                configurationProvider.Object,
                _updatesQueue,
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
                                      //_valuesCache.GetValues,
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
                                  //_valuesCache.GetValues,
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
                                  //_valuesCache.GetValues,
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
            const int sensorValuesCount = 10;

            var barSensorValues = AddAndGetBarSensorValues(sensorValuesCount, type);
            barSensorValues.Add(AddAndGetUnitedSensorValue(type, true));

            var history = _monitoringCore.GetAllSensorHistory(_testProductName, barSensorValues[0].Path);

            TestBarSensorsHistoryData(barSensorValues, history, sensorValuesCount);
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
            const int sensorValuesCount = 10;

            DateTime from = DateTime.UtcNow;

            var barSensorValues = AddAndGetBarSensorValues(sensorValuesCount, type);
            barSensorValues.Add(AddAndGetUnitedSensorValue(type, true));

            DateTime to = DateTime.UtcNow;

            var history = _monitoringCore.GetSensorHistory(_testProductName, barSensorValues[0].Path, from, to);

            TestBarSensorsHistoryData(barSensorValues, history, sensorValuesCount);
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

            var barSensorValues = AddAndGetBarSensorValues(expectedCount, type);
            barSensorValues.Add(AddAndGetUnitedSensorValue(type, true));
            barSensorValues = barSensorValues.TakeLast(specificCount).ToList();

            var history = _monitoringCore.GetSensorHistory(_testProductName, barSensorValues[0].Path, specificCount);

            TestBarSensorsHistoryData(barSensorValues, history, specificCount - 1);
        }


        [Fact]
        [Trait("Category", "Get FileSensor data (content & extension)")]
        public void GetFileSensorValueBytesTest()
        {
            var (content, extension) = _monitoringCore.GetFileSensorValueData(RandomGenerator.GetRandomString(), RandomGenerator.GetRandomString());

            Assert.Empty(content);
            Assert.Empty(extension);
        }

        [Theory]
        [InlineData(SensorType.FileSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "Get FileSensor data (content & extension)")]
        public void GetFileSensorContentTest(SensorType type)
        {
            (byte[] expectedContent, string expectedExtension, string path) = AddFileSensorAndGetItsContentExtensionAndPath(type);

            var (actualContent, actualExtension) = _monitoringCore.GetFileSensorValueData(TestProductsManager.ProductName, path);

            Assert.Equal(expectedContent, actualContent);
            Assert.Equal(expectedExtension, actualExtension);
        }


        [Fact]
        [Trait("Category", "File sensor bytes compressing/decompressing content")]
        public void FileSensorBytesCompressingDecompressingContentTest()
        {
            var sensorValue = _sensorValuesFactory.BuildFileSensorBytesValue();
            var originalContent = sensorValue.FileContent;

            _monitoringCore.AddFileSensor(sensorValue);

            Assert.NotEqual(sensorValue.FileContent, originalContent);

            var (actualContent, _) = _monitoringCore.GetFileSensorValueData(TestProductsManager.ProductName, sensorValue.Path);

            Assert.Equal(originalContent.Length, actualContent.Length);
            Assert.Equal(originalContent, actualContent);
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
            /*GetQueueValues getQueueValues,*/ GetAllSensorHistory getAllSensorHistory,
            GetSensorInfoFromDB getSensorFromDB, SensorValuesTester tester)
        {
            string sensorValuePath = sensorValue.Path;

            Assert.False(isSensorRegistered(productName, sensorValuePath));
            Assert.Null(getSensorInfo(productName, sensorValuePath));
            Assert.Null(getProductSensors(productName).FirstOrDefault(s => s.Path == sensorValuePath));
            //Assert.Empty(getQueueValues(new List<string>() { productName }));
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

        private static void TestBarSensorsHistoryData(List<SensorValueBase> expected, List<SensorHistoryData> actual, int sensorValuesCount)
        {
            Assert.Equal(expected.Count, actual.Count);
            Assert.DoesNotContain(JsonSerializer.Serialize(DateTime.MinValue), actual[sensorValuesCount].TypedData);

            for (int i = 0; i < sensorValuesCount; ++i)
                SensorValuesTester.TestSensorHistoryDataFromDB(expected[i], actual[i]);
        }


        private (byte[], string, string) AddFileSensorAndGetItsContentExtensionAndPath(SensorType type)
        {
            var dataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(type);

            _databaseAdapterManager.DatabaseAdapter.PutSensorData(dataEntity, TestProductsManager.ProductName);

            switch (dataEntity.DataType)
            {
                case (byte)SensorType.FileSensor:
                    var fileSensorData = JsonSerializer.Deserialize<FileSensorData>(dataEntity.TypedData);
                    return (Encoding.UTF8.GetBytes(fileSensorData.FileContent), fileSensorData.Extension, dataEntity.Path);
                case (byte)SensorType.FileSensorBytes:
                    var fileSensorBytesData = JsonSerializer.Deserialize<FileSensorBytesData>(dataEntity.TypedData);
                    return (fileSensorBytesData.FileContent, fileSensorBytesData.Extension, dataEntity.Path);
                default:
                    return (Array.Empty<byte>(), string.Empty, null);
            }
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

            for (int i = 0; i < count; ++i)
            {
                var unaccountedSensorValueWithMinEndTime = _sensorValuesFactory.BuildUnitedSensorValue(type, true);
                _monitoringCore.AddSensorValue(unaccountedSensorValueWithMinEndTime);

                barSensorValues.Add(AddAndGetUnitedSensorValue(type));
            }

            return barSensorValues;
        }

        private UnitedSensorValue AddAndGetUnitedSensorValue(SensorType type, bool useMinEndTime = false)
        {
            var sensorValue = _sensorValuesFactory.BuildUnitedSensorValue(type, useMinEndTime);

            _monitoringCore.AddSensorValue(sensorValue);

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
