using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
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


        public MonitoringDataReceiverTests()
        {
            _valuesCache = new ValuesCache();

            _databaseAdapterManager = new DatabaseAdapterManager();
            _databaseAdapterManager.CreateDatabaseWithTestProduct();

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
        public async void AddBoolSensorValueTest()
        {
            var boolSensorValue = _sensorValuesFactory.BuildBoolSensorValue();

            _monitoringCore.AddSensorValue(boolSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.BooleanSensor);
            _sensorValuesTester.TestSensorDataFromCache(boolSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue(_databaseAdapterManager.TestProduct.Name, boolSensorValue.Path);
            _sensorValuesTester.TestSensorHistoryDataFromDB(boolSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseAdapterManager.DatabaseAdapter.GetSensorInfo(_databaseAdapterManager.TestProduct.Name, boolSensorValue.Path);
            _sensorValuesTester.TestSensorInfoFromDB(boolSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddIntSensorValueTest()
        {
            var intSensorValue = _sensorValuesFactory.BuildIntSensorValue();

            _monitoringCore.AddSensorValue(intSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.IntSensor);
            _sensorValuesTester.TestSensorDataFromCache(intSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue(_databaseAdapterManager.TestProduct.Name, intSensorValue.Path);
            _sensorValuesTester.TestSensorHistoryDataFromDB(intSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseAdapterManager.DatabaseAdapter.GetSensorInfo(_databaseAdapterManager.TestProduct.Name, intSensorValue.Path);
            _sensorValuesTester.TestSensorInfoFromDB(intSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddDoubleSensorValueTest()
        {
            var doubleSensorValue = _sensorValuesFactory.BuildDoubleSensorValue();

            _monitoringCore.AddSensorValue(doubleSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.DoubleSensor);
            _sensorValuesTester.TestSensorDataFromCache(doubleSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue(_databaseAdapterManager.TestProduct.Name, doubleSensorValue.Path);
            _sensorValuesTester.TestSensorHistoryDataFromDB(doubleSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseAdapterManager.DatabaseAdapter.GetSensorInfo(_databaseAdapterManager.TestProduct.Name, doubleSensorValue.Path);
            _sensorValuesTester.TestSensorInfoFromDB(doubleSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddStringSensorValueTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();

            _monitoringCore.AddSensorValue(stringSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.StringSensor);
            _sensorValuesTester.TestSensorDataFromCache(stringSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue(_databaseAdapterManager.TestProduct.Name, stringSensorValue.Path);
            _sensorValuesTester.TestSensorHistoryDataFromDB(stringSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseAdapterManager.DatabaseAdapter.GetSensorInfo(_databaseAdapterManager.TestProduct.Name, stringSensorValue.Path);
            _sensorValuesTester.TestSensorInfoFromDB(stringSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddIntBarSensorValueTest()
        {
            var intBarSensorValue = _sensorValuesFactory.BuildIntBarSensorValue();

            _monitoringCore.AddSensorValue(intBarSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.IntegerBarSensor);
            _sensorValuesTester.TestSensorDataFromCache(intBarSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue(_databaseAdapterManager.TestProduct.Name, intBarSensorValue.Path);
            _sensorValuesTester.TestSensorHistoryDataFromDB(intBarSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseAdapterManager.DatabaseAdapter.GetSensorInfo(_databaseAdapterManager.TestProduct.Name, intBarSensorValue.Path);
            _sensorValuesTester.TestSensorInfoFromDB(intBarSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddDoubleBarSensorValueTest()
        {
            var doubleBarSensorValue = _sensorValuesFactory.BuildDoubleBarSensorValue();

            _monitoringCore.AddSensorValue(doubleBarSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.DoubleBarSensor);
            _sensorValuesTester.TestSensorDataFromCache(doubleBarSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue(_databaseAdapterManager.TestProduct.Name, doubleBarSensorValue.Path);
            _sensorValuesTester.TestSensorHistoryDataFromDB(doubleBarSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseAdapterManager.DatabaseAdapter.GetSensorInfo(_databaseAdapterManager.TestProduct.Name, doubleBarSensorValue.Path);
            _sensorValuesTester.TestSensorInfoFromDB(doubleBarSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddFileSensorBytesValueTest()
        {
            var fileSensorBytesValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            _monitoringCore.AddSensorValue(fileSensorBytesValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.FileSensorBytes);
            _sensorValuesTester.TestSensorDataFromCache(fileSensorBytesValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue(_databaseAdapterManager.TestProduct.Name, fileSensorBytesValue.Path);
            _sensorValuesTester.TestSensorHistoryDataFromDB(fileSensorBytesValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseAdapterManager.DatabaseAdapter.GetSensorInfo(_databaseAdapterManager.TestProduct.Name, fileSensorBytesValue.Path);
            _sensorValuesTester.TestSensorInfoFromDB(fileSensorBytesValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddFileSensorValueTest()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();

            _monitoringCore.AddSensorValue(fileSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseAdapterManager.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.FileSensor);
            _sensorValuesTester.TestSensorDataFromCache(fileSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseAdapterManager.DatabaseAdapter.GetOneValueSensorValue(_databaseAdapterManager.TestProduct.Name, fileSensorValue.Path);
            _sensorValuesTester.TestSensorHistoryDataFromDB(fileSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseAdapterManager.DatabaseAdapter.GetSensorInfo(_databaseAdapterManager.TestProduct.Name, fileSensorValue.Path);
            _sensorValuesTester.TestSensorInfoFromDB(fileSensorValue, sensorInfoFromDB);
        }
    }
}
