using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMSensorDataObjects;
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

            FullSensorValueTest(boolSensorValue, SensorType.BooleanSensor,
                                _valuesCache.GetValues,
                                _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddIntSensorValueTest()
        {
            var intSensorValue = _sensorValuesFactory.BuildIntSensorValue();

            _monitoringCore.AddSensorValue(intSensorValue);

            FullSensorValueTest(intSensorValue, SensorType.IntSensor,
                                _valuesCache.GetValues,
                                _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddDoubleSensorValueTest()
        {
            var doubleSensorValue = _sensorValuesFactory.BuildDoubleSensorValue();

            _monitoringCore.AddSensorValue(doubleSensorValue);

            FullSensorValueTest(doubleSensorValue, SensorType.DoubleSensor,
                                _valuesCache.GetValues,
                                _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddStringSensorValueTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();

            _monitoringCore.AddSensorValue(stringSensorValue);

            FullSensorValueTest(stringSensorValue, SensorType.StringSensor,
                                _valuesCache.GetValues,
                                _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddIntBarSensorValueTest()
        {
            var intBarSensorValue = _sensorValuesFactory.BuildIntBarSensorValue();

            _monitoringCore.AddSensorValue(intBarSensorValue);

            FullSensorValueTest(intBarSensorValue, SensorType.IntegerBarSensor,
                                _valuesCache.GetValues,
                                _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddDoubleBarSensorValueTest()
        {
            var doubleBarSensorValue = _sensorValuesFactory.BuildDoubleBarSensorValue();

            _monitoringCore.AddSensorValue(doubleBarSensorValue);

            FullSensorValueTest(doubleBarSensorValue, SensorType.DoubleBarSensor,
                                _valuesCache.GetValues,
                                _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddFileSensorBytesValueTest()
        {
            var fileSensorBytesValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            _monitoringCore.AddSensorValue(fileSensorBytesValue);

            FullSensorValueTest(fileSensorBytesValue, SensorType.FileSensorBytes,
                                _valuesCache.GetValues,
                                _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        public void AddFileSensorValueTest()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();

            _monitoringCore.AddSensorValue(fileSensorValue);

            FullSensorValueTest(fileSensorValue, SensorType.FileSensor,
                                _valuesCache.GetValues,
                                _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue,
                                _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }


        private async void FullSensorValueTest(SensorValueBase sensorValue, SensorType sensorType,
            GetValuesFromCache getCachedValues, GetSensorHistoryData getSensorHistoryData, GetSensorInfo getSensorInfo)
        {
            await Task.Delay(100);

            TestSensorDataFromCache(sensorValue, sensorType, getCachedValues);
            TestSensorHistoryDataFromDB(sensorValue, getSensorHistoryData);
            TestSensorInfoFromDB(sensorValue, getSensorInfo);
        }

        private void TestSensorDataFromCache(SensorValueBase sensorValue, SensorType sensorType, GetValuesFromCache getCachedValues)
        {
            var sensorDataFromCache = getCachedValues?.Invoke(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                                                     ?.FirstOrDefault(s => s.SensorType == sensorType);

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
