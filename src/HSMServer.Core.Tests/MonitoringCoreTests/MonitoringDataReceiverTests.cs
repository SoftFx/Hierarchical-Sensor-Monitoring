using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.SensorsDataProcessor;
using HSMServer.Core.SensorsDataValidation;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    public class MonitoringDataReceiverTests : IClassFixture<MonitoringDataReceiverFixture>, IDisposable
    {
        private const int SeveralSensorValuesCount = 3;

        private readonly MonitoringCore _monitoringCore;
        private readonly DatabaseAdapterManager _databaseAdapterManager;
        private readonly ValuesCache _valuesCache;

        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly SensorValuesTester _sensorValuesTester;

        private delegate List<SensorData> GetValuesFromCache(List<string> products);
        private delegate SensorHistoryData GetSensorHistoryData(string productName, string path);
        private delegate SensorInfo GetSensorInfo(string productName, string path);


        public MonitoringDataReceiverTests()
        {
            _valuesCache = new ValuesCache();

            _databaseAdapterManager = new DatabaseAdapterManager();
            _databaseAdapterManager.AddTestProduct();

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

        public void Dispose() => _databaseAdapterManager.ClearDatabase();


        [Fact]
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
        public void AddFileSensorValueTest()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();

            _monitoringCore.AddSensorValue(fileSensorValue);

            FullSensorValueTestAsync(fileSensorValue,
                                     _valuesCache.GetValues,
                                     _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                     _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }


        [Fact]
        public void AddSeveralBoolSensorValuesTest()
        {
            var boolSensorValues = new List<BoolSensorValue>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                boolSensorValues.Add(_sensorValuesFactory.BuildBoolSensorValue());

            boolSensorValues.ForEach(s => _monitoringCore.AddSensorValue(s));

            FullSeveralSensorValuesTestAsync(boolSensorValues.Select(s => (SensorValueBase)s),
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                             _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddSeveralIntSensorValuesTest()
        {
            var intSensorValues = new List<IntSensorValue>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                intSensorValues.Add(_sensorValuesFactory.BuildIntSensorValue());

            intSensorValues.ForEach(s => _monitoringCore.AddSensorValue(s));

            FullSeveralSensorValuesTestAsync(intSensorValues.Select(s => (SensorValueBase)s),
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                             _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddSeveralDoubleSensorValuesTest()
        {
            var doubleSensorValues = new List<DoubleSensorValue>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                doubleSensorValues.Add(_sensorValuesFactory.BuildDoubleSensorValue());

            doubleSensorValues.ForEach(s => _monitoringCore.AddSensorValue(s));

            FullSeveralSensorValuesTestAsync(doubleSensorValues.Select(s => (SensorValueBase)s),
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                             _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddSeveralStringSensorValuesTest()
        {
            var stringSensorValues = new List<StringSensorValue>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                stringSensorValues.Add(_sensorValuesFactory.BuildStringSensorValue());

            stringSensorValues.ForEach(s => _monitoringCore.AddSensorValue(s));

            FullSeveralSensorValuesTestAsync(stringSensorValues.Select(s => (SensorValueBase)s),
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                             _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddSeveralIntBarSensorValuesTest()
        {
            var intBarSensorValues = new List<IntBarSensorValue>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                intBarSensorValues.Add(_sensorValuesFactory.BuildIntBarSensorValue());

            intBarSensorValues.ForEach(s => _monitoringCore.AddSensorValue(s));

            FullSeveralSensorValuesTestAsync(intBarSensorValues.Select(s => (SensorValueBase)s),
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                             _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddSeveralDoubleBarSensorValuesTest()
        {
            var doubleBarSensorValues = new List<DoubleBarSensorValue>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                doubleBarSensorValues.Add(_sensorValuesFactory.BuildDoubleBarSensorValue());

            doubleBarSensorValues.ForEach(s => _monitoringCore.AddSensorValue(s));

            FullSeveralSensorValuesTestAsync(doubleBarSensorValues.Select(s => (SensorValueBase)s),
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                             _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddSeveralFileSensorBytesValuesTest()
        {
            var fileSensorBytesValues = new List<FileSensorBytesValue>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                fileSensorBytesValues.Add(_sensorValuesFactory.BuildFileSensorBytesValue());

            fileSensorBytesValues.ForEach(s => _monitoringCore.AddSensorValue(s));

            FullSeveralSensorValuesTestAsync(fileSensorBytesValues.Select(s => (SensorValueBase)s),
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                             _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddSeveralFileSensorValuesTest()
        {
            var fileSensorValues = new List<FileSensorValue>(SeveralSensorValuesCount);
            for (int i = 0; i < SeveralSensorValuesCount; ++i)
                fileSensorValues.Add(_sensorValuesFactory.BuildFileSensorValue());

            fileSensorValues.ForEach(s => _monitoringCore.AddSensorValue(s));

            FullSeveralSensorValuesTestAsync(fileSensorValues.Select(s => (SensorValueBase)s),
                                             _valuesCache.GetValues,
                                             _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                             _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }


        private async void FullSensorValueTestAsync(SensorValueBase sensorValue, GetValuesFromCache getCachedValues,
            GetSensorHistoryData getSensorHistoryData, GetSensorInfo getSensorInfo)
        {
            await Task.Delay(100);

            FullSensorValueTest(sensorValue, getCachedValues, getSensorHistoryData, getSensorInfo);
        }

        private async void FullSeveralSensorValuesTestAsync(IEnumerable<SensorValueBase> sensorValues,
            GetValuesFromCache getCachedValues, GetSensorHistoryData getSensorHistoryData, GetSensorInfo getSensorInfo)
        {
            await Task.Delay(100);

            foreach (var sensorValue in sensorValues)
                FullSensorValueTest(sensorValue, getCachedValues, getSensorHistoryData, getSensorInfo);
        }

        private void FullSensorValueTest(SensorValueBase sensorValue, GetValuesFromCache getCachedValues,
            GetSensorHistoryData getSensorHistoryData, GetSensorInfo getSensorInfo)
        {
            TestSensorDataFromCache(sensorValue, getCachedValues);
            TestSensorHistoryDataFromDB(sensorValue, getSensorHistoryData);
            TestSensorInfoFromDB(sensorValue, getSensorInfo);
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
    }
}
