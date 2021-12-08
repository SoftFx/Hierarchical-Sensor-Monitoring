using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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


        #region Add one sensor value tests

        [Fact]
        [Trait("Category", "One")]
        public void AddBoolSensorValueTest()
        {
            var boolSensorValue = _sensorValuesFactory.BuildBoolSensorValue();

            _monitoringCore.AddSensorValue(boolSensorValue);

            FullSensorValueTestAsync(boolSensorValue,
                                     _valuesCache.GetValues,
                                     _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                     _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        [Trait("Category", "One")]
        public void AddIntSensorValueTest()
        {
            var intSensorValue = _sensorValuesFactory.BuildIntSensorValue();

            _monitoringCore.AddSensorValue(intSensorValue);

            FullSensorValueTestAsync(intSensorValue,
                                     _valuesCache.GetValues,
                                     _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                     _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        [Trait("Category", "One")]
        public void AddDoubleSensorValueTest()
        {
            var doubleSensorValue = _sensorValuesFactory.BuildDoubleSensorValue();

            _monitoringCore.AddSensorValue(doubleSensorValue);

            FullSensorValueTestAsync(doubleSensorValue,
                                     _valuesCache.GetValues,
                                     _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                     _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        [Trait("Category", "One")]
        public void AddStringSensorValueTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();

            _monitoringCore.AddSensorValue(stringSensorValue);

            FullSensorValueTestAsync(stringSensorValue,
                                     _valuesCache.GetValues,
                                     _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                     _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        [Trait("Category", "One")]
        public void AddIntBarSensorValueTest()
        {
            var intBarSensorValue = _sensorValuesFactory.BuildIntBarSensorValue();

            _monitoringCore.AddSensorValue(intBarSensorValue);

            FullSensorValueTestAsync(intBarSensorValue,
                                     _valuesCache.GetValues,
                                     _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                     _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        [Trait("Category", "One")]
        public void AddDoubleBarSensorValueTest()
        {
            var doubleBarSensorValue = _sensorValuesFactory.BuildDoubleBarSensorValue();

            _monitoringCore.AddSensorValue(doubleBarSensorValue);

            FullSensorValueTestAsync(doubleBarSensorValue,
                                     _valuesCache.GetValues,
                                     _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                     _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        [Trait("Category", "One")]
        public void AddFileSensorBytesValueTest()
        {
            var fileSensorBytesValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            _monitoringCore.AddSensorValue(fileSensorBytesValue);

            FullSensorValueTestAsync(fileSensorBytesValue,
                                     _valuesCache.GetValues,
                                     _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                     _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        [Trait("Category", "One")]
        public void AddFileSensorValueTest()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();

            _monitoringCore.AddSensorValue(fileSensorValue);

            FullSensorValueTestAsync(fileSensorValue,
                                     _valuesCache.GetValues,
                                     _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                     _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        #endregion

        #region Add several sensor values tests

        [Fact]
        [Trait("Category", "Several")]
        public void AddSeveralBoolSensorValuesTest()
        {
            var boolSensorValues = new List<SensorValueBase>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                boolSensorValues.Add(_sensorValuesFactory.BuildBoolSensorValue());

            boolSensorValues.ForEach(MonitoringCoreAddSensorValue);

            FullSeveralSensorValuesTestAsync(boolSensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Several")]
        public void AddSeveralIntSensorValuesTest()
        {
            var intSensorValues = new List<SensorValueBase>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                intSensorValues.Add(_sensorValuesFactory.BuildIntSensorValue());

            intSensorValues.ForEach(MonitoringCoreAddSensorValue);

            FullSeveralSensorValuesTestAsync(intSensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Several")]
        public void AddSeveralDoubleSensorValuesTest()
        {
            var doubleSensorValues = new List<SensorValueBase>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                doubleSensorValues.Add(_sensorValuesFactory.BuildDoubleSensorValue());

            doubleSensorValues.ForEach(MonitoringCoreAddSensorValue);

            FullSeveralSensorValuesTestAsync(doubleSensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Several")]
        public void AddSeveralStringSensorValuesTest()
        {
            var stringSensorValues = new List<SensorValueBase>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                stringSensorValues.Add(_sensorValuesFactory.BuildStringSensorValue());

            stringSensorValues.ForEach(MonitoringCoreAddSensorValue);

            FullSeveralSensorValuesTestAsync(stringSensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Several")]
        public void AddSeveralIntBarSensorValuesTest()
        {
            var intBarSensorValues = new List<SensorValueBase>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                intBarSensorValues.Add(_sensorValuesFactory.BuildIntBarSensorValue());

            intBarSensorValues.ForEach(MonitoringCoreAddSensorValue);

            FullSeveralSensorValuesTestAsync(intBarSensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Several")]
        public void AddSeveralDoubleBarSensorValuesTest()
        {
            var doubleBarSensorValues = new List<SensorValueBase>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                doubleBarSensorValues.Add(_sensorValuesFactory.BuildDoubleBarSensorValue());

            doubleBarSensorValues.ForEach(MonitoringCoreAddSensorValue);

            FullSeveralSensorValuesTestAsync(doubleBarSensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Several")]
        public void AddSeveralFileSensorBytesValuesTest()
        {
            var fileSensorBytesValues = new List<SensorValueBase>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                fileSensorBytesValues.Add(_sensorValuesFactory.BuildFileSensorBytesValue());

            fileSensorBytesValues.ForEach(MonitoringCoreAddSensorValue);

            FullSeveralSensorValuesTestAsync(fileSensorBytesValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Several")]
        public void AddSeveralFileSensorValuesTest()
        {
            var fileSensorValues = new List<SensorValueBase>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                fileSensorValues.Add(_sensorValuesFactory.BuildFileSensorValue());

            fileSensorValues.ForEach(MonitoringCoreAddSensorValue);

            FullSeveralSensorValuesTestAsync(fileSensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        #endregion

        #region Add different sensor values tests

        [Fact]
        [Trait("Category", "Random")]
        public void Add10RandomSensorValuesTest()
        {
            var sensorValues = GetRandomSensorValues(10);

            sensorValues.ForEach(s => MonitoringCoreAddSensorValue(s));

            FullSeveralSensorValuesTestAsync(sensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Random")]
        public void Add50RandomSensorValuesTest()
        {
            var sensorValues = GetRandomSensorValues(50);

            sensorValues.ForEach(s => MonitoringCoreAddSensorValue(s));

            FullSeveralSensorValuesTestAsync(sensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Random")]
        public void Add100RandomSensorValuesTest()
        {
            var sensorValues = GetRandomSensorValues(100);

            sensorValues.ForEach(s => MonitoringCoreAddSensorValue(s));

            FullSeveralSensorValuesTestAsync(sensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Random")]
        public void Add500RandomSensorValuesTest()
        {
            var sensorValues = GetRandomSensorValues(500);

            sensorValues.ForEach(s => MonitoringCoreAddSensorValue(s));

            FullSeveralSensorValuesTestAsync(sensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        [Fact]
        [Trait("Category", "Random")]
        public void Add1000RandomSensorValuesTest()
        {
            var sensorValues = GetRandomSensorValues(1000);

            sensorValues.ForEach(s => MonitoringCoreAddSensorValue(s));

            FullSeveralSensorValuesTestAsync(sensorValues,
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetAllSensorHistory,
                                             _databaseAdapterManager.DatabaseAdapter.GetProductSensors);
        }

        #endregion


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
