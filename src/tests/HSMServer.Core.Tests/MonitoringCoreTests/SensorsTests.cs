using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model.Sensor;
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


        public SensorsTests(SensorsFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            var configurationProvider = new Mock<IConfigurationProvider>();

            //var monitoringLogger = CommonMoqs.CreateNullLogger<MonitoringCore>();
            //_monitoringCore = new MonitoringCore(
            //    _databaseCoreManager.DatabaseCore,
            //    configurationProvider.Object,
            //    monitoringLogger);
        }


        //[Theory]
        //[InlineData(1)]
        //[InlineData(100)]
        //[Trait("Category", "Get Sensors History Data")]
        //public void GetAllSensorsHistoryDataTest(int count)
        //{
        //    var sensorValues = AddRandomSensorValuesAndGetTheirValues(count);

        //    foreach (var sensorValue in sensorValues)
        //    {
        //        var history = _monitoringCore.GetAllSensorHistory(_testProductName, sensorValue.Key);

        //        for (int i = 0; i < sensorValue.Value.Count; ++i)
        //            SensorValuesTester.TestSensorHistoryDataFromDB(sensorValue.Value[i], history[i]);
        //    }
        //}

        //[Theory]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[Trait("Category", "Get Sensors History Data")]
        //public void GetAllSensorsHistoryData_WithBarValues_Test(SensorType type)
        //{
        //    const int sensorValuesCount = 10;

        //    var barSensorValues = AddAndGetBarSensorValues(sensorValuesCount, type);
        //    barSensorValues.Add(AddAndGetUnitedSensorValue(type, true));

        //    var history = _monitoringCore.GetAllSensorHistory(_testProductName, barSensorValues[0].Path);

        //    TestBarSensorsHistoryData(barSensorValues, history, sensorValuesCount);
        //}

        //[Theory]
        //[InlineData(1)]
        //[InlineData(100)]
        //[Trait("Category", "Get Sensors History Data")]
        //public void GetSensorsHistoryDataFromToTest(int count)
        //{
        //    DateTime from = DateTime.UtcNow;

        //    var sensorValues = AddRandomSensorValuesAndGetTheirValues(count);

        //    DateTime to = DateTime.UtcNow;

        //    foreach (var sensorValue in sensorValues)
        //    {
        //        var history = _monitoringCore.GetSensorHistory(_testProductName, sensorValue.Key, from, to);

        //        for (int i = 0; i < sensorValue.Value.Count; ++i)
        //            SensorValuesTester.TestSensorHistoryDataFromDB(sensorValue.Value[i], history[i]);
        //    }
        //}

        //[Theory]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[Trait("Category", "Get Sensors History Data")]
        //public void GetSensorsHistoryDataFromTo_WithBarValues_Test(SensorType type)
        //{
        //    const int sensorValuesCount = 10;

        //    DateTime from = DateTime.UtcNow;

        //    var barSensorValues = AddAndGetBarSensorValues(sensorValuesCount, type);
        //    barSensorValues.Add(AddAndGetUnitedSensorValue(type, true));

        //    DateTime to = DateTime.UtcNow;

        //    var history = _monitoringCore.GetSensorHistory(_testProductName, barSensorValues[0].Path, from, to);

        //    TestBarSensorsHistoryData(barSensorValues, history, sensorValuesCount);
        //}

        //[Theory]
        //[InlineData(1)]
        //[InlineData(100)]
        //[Trait("Category", "Get Sensors History Data")]
        //public void GetSensorsHistoryDataSpecificCountTest(int count)
        //{
        //    const int specificCount = 5;

        //    var sensorValues = AddRandomSensorValuesAndGetTheirValues(count, specificCount);

        //    foreach (var sensorValue in sensorValues)
        //    {
        //        var history = _monitoringCore.GetSensorHistory(_testProductName, sensorValue.Key, specificCount);

        //        for (int i = 0; i < sensorValue.Value.Count; ++i)
        //            SensorValuesTester.TestSensorHistoryDataFromDB(sensorValue.Value[i], history[i]);
        //    }
        //}

        //[Theory]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[Trait("Category", "Get Sensors History Data")]
        //public void GetSensorsHistoryDataSpecificCount_WithBarValues_Test(SensorType type)
        //{
        //    const int expectedCount = 101;
        //    const int specificCount = 5;

        //    var barSensorValues = AddAndGetBarSensorValues(expectedCount, type);
        //    barSensorValues.Add(AddAndGetUnitedSensorValue(type, true));
        //    barSensorValues = barSensorValues.TakeLast(specificCount).ToList();

        //    var history = _monitoringCore.GetSensorHistory(_testProductName, barSensorValues[0].Path, specificCount);

        //    TestBarSensorsHistoryData(barSensorValues, history, specificCount - 1);
        //}


        //[Fact]
        //[Trait("Category", "Get FileSensor data (content & extension)")]
        //public void GetFileSensorValueBytesTest()
        //{
        //    var (content, extension) = _monitoringCore.GetFileSensorValueData(RandomGenerator.GetRandomString(), RandomGenerator.GetRandomString());

        //    Assert.Empty(content);
        //    Assert.Empty(extension);
        //}

        //[Theory]
        //[InlineData(SensorType.FileSensor)]
        //[InlineData(SensorType.FileSensorBytes)]
        //[Trait("Category", "Get FileSensor data (content & extension)")]
        //public void GetFileSensorContentTest(SensorType type)
        //{
        //    (byte[] expectedContent, string expectedExtension, string path) = AddFileSensorAndGetItsContentExtensionAndPath(type);

        //    var (actualContent, actualExtension) = _monitoringCore.GetFileSensorValueData(TestProductsManager.ProductName, path);

        //    Assert.Equal(expectedContent, actualContent);
        //    Assert.Equal(expectedExtension, actualExtension);
        //}


        //[Fact]
        //[Trait("Category", "File sensor bytes compressing/decompressing content")]
        //public void FileSensorBytesCompressingDecompressingContentTest()
        //{
        //    var sensorValue = _sensorValuesFactory.BuildFileSensorBytesValue();
        //    var originalContent = sensorValue.FileContent;

        //    _monitoringCore.AddSensorValue(sensorValue);

        //    Assert.NotEqual(sensorValue.FileContent, originalContent);

        //    var (actualContent, _) = _monitoringCore.GetFileSensorValueData(TestProductsManager.ProductName, sensorValue.Path);

        //    Assert.Equal(originalContent.Length, actualContent.Length);
        //    Assert.Equal(originalContent, actualContent);
        //}


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

            _databaseCoreManager.DatabaseCore.PutSensorData(dataEntity, TestProductsManager.ProductName);

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

        //private SensorValueBase AddAndGetRandomSensorValue(string specificPathPart = null)
        //{
        //    var sensorValue = _sensorValuesFactory.BuildRandomSensorValue();

        //    if (!string.IsNullOrEmpty(specificPathPart))
        //        sensorValue.Path = $"{sensorValue.Path}{specificPathPart}";

        //    _monitoringCore.AddSensorValue(sensorValue);

        //    return sensorValue;
        //}

        //private Dictionary<string, List<SensorValueBase>> AddRandomSensorValuesAndGetTheirValues(int countToAdd, int? specificCount = null)
        //{
        //    if (!specificCount.HasValue)
        //        specificCount = countToAdd;

        //    var sensorValues = new List<SensorValueBase>();

        //    for (int i = 0; i < countToAdd; ++i)
        //        sensorValues.Add(AddAndGetRandomSensorValue());

        //    return sensorValues.GroupBy(s => s.Path)
        //                       .ToDictionary(s => s.Key, s => s.TakeLast(specificCount.Value).ToList());
        //}

        //private List<SensorValueBase> AddAndGetBarSensorValues(int count, SensorType type)
        //{
        //    var barSensorValues = new List<SensorValueBase>(count);

        //    for (int i = 0; i < count; ++i)
        //    {
        //        var unaccountedSensorValueWithMinEndTime = _sensorValuesFactory.BuildUnitedSensorValue(type, true);
        //        _monitoringCore.AddSensorValue(unaccountedSensorValueWithMinEndTime);

        //        barSensorValues.Add(AddAndGetUnitedSensorValue(type));
        //    }

        //    return barSensorValues;
        //}

        //private UnitedSensorValue AddAndGetUnitedSensorValue(SensorType type, bool useMinEndTime = false)
        //{
        //    var sensorValue = _sensorValuesFactory.BuildUnitedSensorValue(type, useMinEndTime);

        //    _monitoringCore.AddSensorValue(sensorValue);

        //    return sensorValue;
        //}
    }
}
