using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.SensorsDataProcessor;
using HSMServer.Core.SensorsDataValidation;
using HSMServer.Core.Tests.Infrastructure;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    public class MonitoringDataReceiverTests : IClassFixture<MonitoringDataReceiverFixture>
    {
        private const int SeveralSensorValuesCount = 3;

        private readonly MonitoringCore _monitoringCore;
        private readonly DatabaseAdapterManager _databaseAdapterManager;
        private readonly ValuesCache _valuesCache;

        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly SensorValuesTester _sensorValuesTester;

        private delegate List<SensorData> GetValuesFromCache(List<string> products);
        private delegate SensorHistoryData GetSensorHistoryData(string productName, string path);
        private delegate List<SensorHistoryData> GetAllSensorHistoryData(string productName, string path);
        private delegate SensorInfo GetSensorInfo(string productName, string path);
        private delegate List<SensorInfo> GetAllSensorInfo(Product product);


        public MonitoringDataReceiverTests(MonitoringDataReceiverFixture fixture)
        {
            _valuesCache = new ValuesCache();

            _databaseAdapterManager = new DatabaseAdapterManager();
            _databaseAdapterManager.AddTestProduct();
            fixture.CreatedDatabases.Add(_databaseAdapterManager);

            _sensorValuesFactory = new SensorValuesFactory(_databaseAdapterManager);
            _sensorValuesTester = new SensorValuesTester(_databaseAdapterManager);

            var userManager = new Mock<IUserManager>();

            var barSensorsStorage = new BarSensorsStorage();

            var converterLogger = CommonMoqs.CreateNullLogger<Converter>();
            var converter = new Converter(converterLogger);

            var configProviderLogger = CommonMoqs.CreateNullLogger<ConfigurationProvider>();
            var configurationProvider = new ConfigurationProvider(_databaseAdapterManager.DatabaseAdapter, configProviderLogger);

            var productManagerLogger = CommonMoqs.CreateNullLogger<ProductManager>();
            var productManager = new ProductManager(_databaseAdapterManager.DatabaseAdapter, converter, productManagerLogger);

            var sensorDataValidatorLogger = CommonMoqs.CreateNullLogger<SensorsDataValidator>();
            var sensorDataValidator = new SensorsDataValidator(configurationProvider, _databaseAdapterManager.DatabaseAdapter,
                productManager, sensorDataValidatorLogger);

            var sensorsProcessorLogger = CommonMoqs.CreateNullLogger<SensorsProcessor>();
            var sensorsProcessor = new SensorsProcessor(sensorsProcessorLogger, converter, sensorDataValidator, productManager);

            var monitoringLogger = CommonMoqs.CreateNullLogger<MonitoringCore>();
            _monitoringCore = new MonitoringCore(
                _databaseAdapterManager.DatabaseAdapter,
                userManager.Object,
                barSensorsStorage,
                productManager,
                sensorsProcessor,
                configurationProvider,
                _valuesCache,
                converter,
                monitoringLogger);
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [InlineData(SensorType.FileSensor)]
        [Trait("Category", "One")]
        public void AddSensorValueTest(SensorType type)
        {
            var sensorValue = _sensorValuesFactory.BuildSensorValue(type);

            MonitoringCoreAddSensorValue(sensorValue);

            FullSensorValueTestAsync(sensorValue,
                                     _valuesCache.GetValues,
                                     _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                     _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [InlineData(SensorType.FileSensor)]
        [Trait("Category", "Several")]
        public void AddSeveralSensorValuesTest(SensorType type)
        {
            var sensorValues = new List<SensorValueBase>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                sensorValues.Add(_sensorValuesFactory.BuildSensorValue(type));

            sensorValues.ForEach(MonitoringCoreAddSensorValue);

            FullSeveralSensorValuesTestAsync(sensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }


        [Theory]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        [Trait("Category", "Random")]
        public void AddRandomSensorValuesTest(int count)
        {
            var sensorValues = GetRandomSensorValues(count);

            sensorValues.ForEach(s => MonitoringCoreAddSensorValue(s));

            FullSeveralSensorValuesTestAsync(sensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }


        private void MonitoringCoreAddSensorValue(SensorValueBase sensorValue)
        {
            switch (sensorValue)
            {
                case BoolSensorValue boolSensorValue:
                    _monitoringCore.AddSensorValue(boolSensorValue);
                    break;
                case IntSensorValue intSensorValue:
                    _monitoringCore.AddSensorValue(intSensorValue);
                    break;
                case DoubleSensorValue doubleSensorValue:
                    _monitoringCore.AddSensorValue(doubleSensorValue);
                    break;
                case StringSensorValue stringSensorValue:
                    _monitoringCore.AddSensorValue(stringSensorValue);
                    break;
                case IntBarSensorValue intBarSensorValue:
                    _monitoringCore.AddSensorValue(intBarSensorValue);
                    break;
                case DoubleBarSensorValue doubleBarSensorValue:
                    _monitoringCore.AddSensorValue(doubleBarSensorValue);
                    break;
                case FileSensorBytesValue fileSensorBytesValue:
                    _monitoringCore.AddSensorValue(fileSensorBytesValue);
                    break;
                case FileSensorValue fileSensorValue:
                    _monitoringCore.AddSensorValue(fileSensorValue);
                    break;
            };
        }

        private async void FullSensorValueTestAsync(SensorValueBase sensorValue, GetValuesFromCache getCachedValues,
            GetSensorHistoryData getSensorHistoryData, GetSensorInfo getSensorInfo)
        {
            await Task.Delay(100);

            TestSensorDataFromCache(sensorValue, getCachedValues);
            TestSensorHistoryDataFromDB(sensorValue, getSensorHistoryData);
            TestSensorInfoFromDB(sensorValue, getSensorInfo);
        }

        private async void FullSeveralSensorValuesTestAsync(List<SensorValueBase> sensorValues,
           GetValuesFromCache getCachedValues, GetAllSensorHistoryData getAllSensorHistoryData, GetAllSensorInfo getAllSensorInfo, int? time = null)
        {
            await Task.Delay(sensorValues.Count);

            var sensorsDict = sensorValues.GroupBy(s => s.Path)
                                          .ToDictionary(s => s.Key, s => s.ToList());

            TestSeveralSensorDataFromCache(sensorsDict, getCachedValues);
            TestSeveralSensorHistoryDataFromDB(sensorsDict, getAllSensorHistoryData);
            TestSeveralSensorInfoFromDB(sensorsDict, getAllSensorInfo);
        }

        private void TestSensorDataFromCache(SensorValueBase sensorValue, GetValuesFromCache getCachedValues)
        {
            var sensorDataFromCache = getCachedValues?.Invoke(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                                                     ?.FirstOrDefault(s => s.Path == sensorValue.Path);

            _sensorValuesTester.TestSensorDataFromCache(sensorValue, sensorDataFromCache);
        }

        private void TestSensorHistoryDataFromDB(SensorValueBase sensorValue, GetSensorHistoryData getSensorHistoryData)
        {
            var sensorHistoryDataFromDB = getSensorHistoryData?.Invoke(_databaseAdapterManager.TestProduct.Name, sensorValue.Path);

            SensorValuesTester.TestSensorHistoryDataFromDB(sensorValue, sensorHistoryDataFromDB);
        }

        private void TestSensorInfoFromDB(SensorValueBase sensorValue, GetSensorInfo getSensorInfo)
        {
            var sensorInfoFromDB = getSensorInfo?.Invoke(_databaseAdapterManager.TestProduct.Name, sensorValue.Path);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfoFromDB);
        }

        private void TestSeveralSensorDataFromCache(Dictionary<string, List<SensorValueBase>> sensorValues,
            GetValuesFromCache getValuesFromCache)
        {
            var cache = getValuesFromCache?.Invoke(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                                          ?.ToDictionary(s => s.Path);

            foreach (var sensors in sensorValues)
                _sensorValuesTester.TestSensorDataFromCache(sensors.Value.LastOrDefault(), cache[sensors.Key]);
        }

        private void TestSeveralSensorHistoryDataFromDB(Dictionary<string, List<SensorValueBase>> sensorValues,
            GetAllSensorHistoryData getAllSensorHistoryData)
        {
            foreach (var sensors in sensorValues)
            {
                var datas = getAllSensorHistoryData?.Invoke(_databaseAdapterManager.TestProduct.Name, sensors.Key);
                for (int i = 0; i < sensors.Value.Count; ++i)
                    SensorValuesTester.TestSensorHistoryDataFromDB(sensors.Value[i], datas[i]);
            }
        }

        private void TestSeveralSensorInfoFromDB(Dictionary<string, List<SensorValueBase>> sensorValues,
            GetAllSensorInfo getAllSensorInfo)
        {
            var infos = getAllSensorInfo(_databaseAdapterManager.TestProduct).ToDictionary(s => s.Path);

            foreach (var sensors in sensorValues)
                for (int i = 0; i < sensors.Value.Count; ++i)
                    _sensorValuesTester.TestSensorInfoFromDB(sensors.Value[i], infos[sensors.Key]);
        }

        private List<SensorValueBase> GetRandomSensorValues(int size)
        {
            var sensorValues = new List<SensorValueBase>(size);
            for (int i = 0; i < size; ++i)
                sensorValues.Add(_sensorValuesFactory.BuildRandomSensorValue());

            return sensorValues;
        }
    }
}
