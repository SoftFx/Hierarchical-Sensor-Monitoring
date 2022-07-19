using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    public class MonitoringDataReceiverTests : MonitoringCoreTestsBase<MonitoringDataReceiverFixture>
    {
        private const int SeveralSensorValuesCount = 3;

        private readonly string _testProductName = TestProductsManager.TestProduct.DisplayName;
        private readonly ITreeValuesCache _valuesCache;

        private delegate List<SensorModel> GetSensorsFromCache();
        private delegate SensorHistoryData GetSensorHistoryData(string productName, string path);
        private delegate List<SensorHistoryData> GetAllSensorHistoryData(string productName, string path);
        private delegate List<SensorEntity> GetAllSensorsFromDb();


        public MonitoringDataReceiverTests(MonitoringDataReceiverFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            var configProviderLogger = CommonMoqs.CreateNullLogger<ConfigurationProvider>();
            var configurationProvider = new ConfigurationProvider(_databaseCoreManager.DatabaseCore, configProviderLogger);

            _valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, _userManager, _updatesQueue);

            var monitoringLogger = CommonMoqs.CreateNullLogger<MonitoringCore>();
            _monitoringCore = new MonitoringCore(
                _databaseCoreManager.DatabaseCore,
                configurationProvider,
                monitoringLogger);
        }


        //[Theory]
        //[InlineData(SensorType.BooleanSensor)]
        //[InlineData(SensorType.IntSensor)]
        //[InlineData(SensorType.DoubleSensor)]
        //[InlineData(SensorType.StringSensor)]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[InlineData(SensorType.FileSensorBytes)]
        //[Trait("Category", "One")]
        //public async Task AddSensorValueTest(SensorType type)
        //{
        //    var sensorValue = _sensorValuesFactory.BuildSensorValue(type);

        //    _monitoringCore.AddSensorValue(sensorValue);

        //    await FullSensorValueTestAsync(sensorValue,
        //                                   _valuesCache.GetSensors,
        //                                   _databaseCoreManager.DatabaseCore.GetOneValueSensorValue,
        //                                   _databaseCoreManager.DatabaseCore.GetAllSensors);
        //}

        //[Theory]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[Trait("Category", "One")]
        //public void AddBarSensorValueTest(SensorType type)
        //{
        //    var sensorValue = _sensorValuesFactory.BuildSensorValue(type);
        //    (sensorValue as BarSensorValueBase).EndTime = System.DateTime.MinValue;

        //    _monitoringCore.AddSensorValue(sensorValue);

        //    var lastBarValue = _barStorage.GetLastValue(_testProductName, sensorValue.Path);

        //    Assert.Equal(_testProductName, lastBarValue.ProductName);
        //    Assert.Equal(SensorValuesTester.GetSensorValueType(sensorValue), lastBarValue.ValueType);
        //    Assert.Equal(sensorValue, lastBarValue.Value);
        //}


        //[Theory]
        //[InlineData(SensorType.BooleanSensor)]
        //[InlineData(SensorType.IntSensor)]
        //[InlineData(SensorType.DoubleSensor)]
        //[InlineData(SensorType.StringSensor)]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[InlineData(SensorType.FileSensorBytes)]
        //[Trait("Category", "Several")]
        //public async Task AddSeveralSensorValuesTest(SensorType type)
        //{
        //    var sensorValues = new List<SensorValueBase>(SeveralSensorValuesCount);
        //    for (int i = 0; i < SeveralSensorValuesCount; ++i)
        //        sensorValues.Add(_sensorValuesFactory.BuildSensorValue(type));

        //    sensorValues.ForEach(_monitoringCore.AddSensorValue);

        //    await FullSeveralSensorValuesTestAsync(sensorValues,
        //                                           _valuesCache.GetSensors,
        //                                           _databaseCoreManager.DatabaseCore.GetAllSensorHistory,
        //                                           _databaseCoreManager.DatabaseCore.GetAllSensors);
        //}


        //[Theory]
        //[InlineData(10)]
        //[InlineData(50)]
        //[InlineData(100)]
        //[InlineData(500)]
        //[InlineData(1000)]
        //[Trait("Category", "Random")]
        //public async Task AddRandomSensorValuesTest(int count)
        //{
        //    var sensorValues = GetRandomSensorValues(count);

        //    sensorValues.ForEach(_monitoringCore.AddSensorValue);

        //    await FullSeveralSensorValuesTestAsync(sensorValues,
        //                                           _valuesCache.GetSensors,
        //                                           _databaseCoreManager.DatabaseCore.GetAllSensorHistory,
        //                                           _databaseCoreManager.DatabaseCore.GetAllSensors);
        //}


        //[Theory]
        //[InlineData(SensorType.BooleanSensor)]
        //[InlineData(SensorType.IntSensor)]
        //[InlineData(SensorType.DoubleSensor)]
        //[InlineData(SensorType.StringSensor)]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[Trait("Category", "UnitedSensorValues One")]
        //public async Task AddUnitedSensorValueTest(SensorType sensorType)
        //{
        //    var unitedValue = _sensorValuesFactory.BuildUnitedSensorValue(sensorType);

        //    _monitoringCore.AddSensorValue(unitedValue);

        //    await FullSeveralSensorValuesTestAsync(new List<SensorValueBase>() { unitedValue },
        //                                           _valuesCache.GetSensors,
        //                                           _databaseCoreManager.DatabaseCore.GetAllSensorHistory,
        //                                           _databaseCoreManager.DatabaseCore.GetAllSensors);
        //}

        //[Theory]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[Trait("Category", "UnitedBarSensorValues One")]
        //public void AddUnitedBarSensorValueTest(SensorType type)
        //{
        //    var unitedValue = _sensorValuesFactory.BuildUnitedSensorValue(type, isMinEndTime: true);

        //    _monitoringCore.AddSensorValue(unitedValue);

        //    var lastBarValue = _barStorage.GetLastValue(_testProductName, unitedValue.Path);

        //    Assert.Equal(_testProductName, lastBarValue.ProductName);
        //    Assert.Equal(SensorValuesTester.GetSensorValueType(unitedValue), lastBarValue.ValueType);
        //    SensorValuesTester.TestBarSensorFromUnitedSensor(unitedValue, lastBarValue.Value);
        //}

        //[Theory]
        //[InlineData(10)]
        //[InlineData(50)]
        //[InlineData(100)]
        //[InlineData(500)]
        //[InlineData(1000)]
        //[Trait("Category", "UnitedSensorValues Several Random")]
        //public async Task AddRandomUnitedSensorValuesTest(int count)
        //{
        //    var unitedValues = GetRandomUnitedSensors(count);

        //    unitedValues.ForEach(_monitoringCore.AddSensorValue);

        //    await FullSeveralSensorValuesTestAsync(unitedValues,
        //                                           _valuesCache.GetSensors,
        //                                           _databaseCoreManager.DatabaseCore.GetAllSensorHistory,
        //                                           _databaseCoreManager.DatabaseCore.GetAllSensors);
        //}


        private async Task FullSensorValueTestAsync(SensorValueBase sensorValue, GetSensorsFromCache getSensorsFromCache,
            GetSensorHistoryData getSensorHistoryData, GetAllSensorsFromDb getAllSensorsFromDb)
        {
            await Task.Delay(100);

            TestSensorFromCache(sensorValue, getSensorsFromCache);
            TestSensorHistoryDataFromDB(sensorValue, getSensorHistoryData);
            //TestSensorFromDB(sensorValue, getAllSensorsFromDb);
        }

        private async Task FullSeveralSensorValuesTestAsync(List<SensorValueBase> sensorValues,
           GetSensorsFromCache getSensorsFromCache, GetAllSensorHistoryData getAllSensorHistoryData, GetAllSensorsFromDb getAllSensorsFromDb, int? time = null)
        {
            await Task.Delay(sensorValues.Count);

            var sensorsDict = sensorValues.GroupBy(s => s.Path)
                                          .ToDictionary(s => s.Key, s => s.ToList());

            TestSeveralSensorsFromCache(sensorsDict, getSensorsFromCache);
            TestSeveralSensorHistoryDataFromDB(sensorsDict, getAllSensorHistoryData);
            //TestSeveralSensorsFromDB(sensorsDict, getAllSensorsFromDb);
        }

        private void TestSensorFromCache(SensorValueBase sensorValue, GetSensorsFromCache getCachedSensors)
        {
            var sensorModel = getCachedSensors?.Invoke()?.FirstOrDefault(s => s.Path == sensorValue.Path);

            TestSensorFromCache(sensorValue, sensorModel);
        }

        private void TestSensorHistoryDataFromDB(SensorValueBase sensorValue, GetSensorHistoryData getSensorHistoryData)
        {
            var sensorHistoryDataFromDB = getSensorHistoryData?.Invoke(_testProductName, sensorValue.Path);

            SensorValuesTester.TestSensorHistoryDataFromDB(sensorValue, sensorHistoryDataFromDB);
        }

        //private void TestSensorFromDB(SensorValueBase sensorValue, GetAllSensorsFromDb getAllSensors)
        //{
        //    var sensorFromDB = getAllSensors?.Invoke().FirstOrDefault(s => s.Path == sensorValue.Path);
        //    var parentProduct = _valuesCache.GetProduct(sensorValue.Key);

        //    SensorValuesTester.TestSensorEntity(sensorValue, sensorFromDB);
        //}

        private void TestSeveralSensorsFromCache(Dictionary<string, List<SensorValueBase>> sensorValues,
            GetSensorsFromCache getSensorsFromCache)
        {
            var cache = getSensorsFromCache?.Invoke()?.ToDictionary(s => s.Path);

            foreach (var sensors in sensorValues)
                TestSensorFromCache(sensors.Value.LastOrDefault(), cache[sensors.Key]);
        }

        private void TestSensorFromCache(SensorValueBase sensorValue, SensorModel sensorModel)
        {
            var parentProduct = _valuesCache.GetProduct(sensorValue.Key);

            ModelsTester.TestSensorModel(sensorValue, parentProduct.DisplayName, sensorModel, parentProduct);
            ModelsTester.TestSensorModelData(sensorValue, sensorModel);
        }

        private void TestSeveralSensorHistoryDataFromDB(Dictionary<string, List<SensorValueBase>> sensorValues,
            GetAllSensorHistoryData getAllSensorHistoryData)
        {
            foreach (var sensors in sensorValues)
            {
                var datas = getAllSensorHistoryData?.Invoke(_testProductName, sensors.Key);
                for (int i = 0; i < sensors.Value.Count; ++i)
                    SensorValuesTester.TestSensorHistoryDataFromDB(sensors.Value[i], datas[i]);
            }
        }

        //private void TestSeveralSensorsFromDB(Dictionary<string, List<SensorValueBase>> sensorValues,
        //    GetAllSensorsFromDb getAllSensorsFromDb)
        //{
        //    var sensorEntities = getAllSensorsFromDb().ToDictionary(s => s.Path);

        //    foreach (var sensors in sensorValues)
        //    {
        //        var parentProduct = _valuesCache.GetProduct(sensors.Value.First().Key);
        //        for (int i = 0; i < sensors.Value.Count; ++i)
        //            SensorValuesTester.TestSensorEntity(sensors.Value[i], sensorEntities[sensors.Key]);
        //    }
        //}

        private List<SensorValueBase> GetRandomSensorValues(int size)
        {
            var sensorValues = new List<SensorValueBase>(size);
            for (int i = 0; i < size; ++i)
                sensorValues.Add(_sensorValuesFactory.BuildRandomSensorValue());

            return sensorValues;
        }

        //private List<SensorValueBase> GetRandomUnitedSensors(int size)
        //{
        //    var sensorValues = new List<SensorValueBase>(size);
        //    for (int i = 0; i < size; ++i)
        //        sensorValues.Add(_sensorValuesFactory.BuildRandomUnitedSensorValue());

        //    return sensorValues;
        //}
    }
}
