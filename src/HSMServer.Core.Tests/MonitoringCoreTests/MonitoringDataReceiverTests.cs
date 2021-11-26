using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.SensorsDataProcessor;
using HSMServer.Core.SensorsDataValidation;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests
{
    public class MonitoringDataReceiverTests : IClassFixture<DatabaseAdapterFixture>
    {
        private readonly MonitoringCore _monitoringCore;
        private readonly DatabaseAdapterFixture _databaseFixture;
        private readonly ValuesCache _valuesCache;


        public MonitoringDataReceiverTests(DatabaseAdapterFixture dbFixture)
        {
            _databaseFixture = dbFixture;
            _valuesCache = new ValuesCache();

            var userManager = new Mock<IUserManager>();

            var barSensorsStorage = new BarSensorsStorage();

            var converterLogger = CommonMoqs.CreateNullLogger<Converter>();
            var converter = new Converter(converterLogger);

            var configProviderLogger = CommonMoqs.CreateNullLogger<ConfigurationProvider>();
            var configurationProvider = new ConfigurationProvider(_databaseFixture.DatabaseAdapter, configProviderLogger);

            var productManagerLogger = CommonMoqs.CreateNullLogger<ProductManager>();
            var productManager = new ProductManager(_databaseFixture.DatabaseAdapter, converter, productManagerLogger);

            var sensorDataValidatorLogger = CommonMoqs.CreateNullLogger<SensorsDataValidator>();
            var sensorDataValidator = new SensorsDataValidator(configurationProvider, _databaseFixture.DatabaseAdapter,
                productManager, sensorDataValidatorLogger);

            var sensorsProcessorLogger = CommonMoqs.CreateNullLogger<SensorsProcessor>();
            var sensorsProcessor = new SensorsProcessor(sensorsProcessorLogger, converter, sensorDataValidator, productManager);

            var monitoringLogger = CommonMoqs.CreateNullLogger<MonitoringCore>();
            _monitoringCore = new MonitoringCore(
                _databaseFixture.DatabaseAdapter,
                userManager.Object,
                barSensorsStorage,
                productManager,
                sensorsProcessor,
                configurationProvider,
                _valuesCache,
                converter,
                monitoringLogger);
        }


        [Fact]
        public async void AddBoolSensorValueTest()
        {
            var boolSensorValue = new BoolSensorValue()
            {
                BoolValue = true,
                Key = _databaseFixture.TestProduct.Key,
                Path = nameof(BoolSensorValue),
                Description = $"{nameof(BoolSensorValue)} {nameof(BoolSensorValue.Description)}",
                Comment = $"{nameof(BoolSensorValue)} {nameof(BoolSensorValue.Comment)}",
                Time = DateTime.UtcNow,
            };

            _monitoringCore.AddSensorValue(boolSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseFixture.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.BooleanSensor);
            SensorValuesTester.TestSensorDataFromCache(boolSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(_databaseFixture.TestProduct.Name, boolSensorValue.Path);
            SensorValuesTester.TestSensorHistoryDataFromDB(boolSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(_databaseFixture.TestProduct.Name, boolSensorValue.Path);
            SensorValuesTester.TestSensorInfoFromDB(boolSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddIntSensorValueTest()
        {
            var intSensorValue = new IntSensorValue()
            {
                IntValue = 123,
                Key = _databaseFixture.TestProduct.Key,
                Path = nameof(IntSensorValue),
                Description = $"{nameof(IntSensorValue)} {nameof(IntSensorValue.Description)}",
                Comment = $"{nameof(IntSensorValue)} {nameof(IntSensorValue.Comment)}",
                Time = DateTime.UtcNow,
            };

            _monitoringCore.AddSensorValue(intSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseFixture.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.IntSensor);
            SensorValuesTester.TestSensorDataFromCache(intSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(_databaseFixture.TestProduct.Name, intSensorValue.Path);
            SensorValuesTester.TestSensorHistoryDataFromDB(intSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(_databaseFixture.TestProduct.Name, intSensorValue.Path);
            SensorValuesTester.TestSensorInfoFromDB(intSensorValue, sensorInfoFromDB);
        }
    }
}
