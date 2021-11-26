using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMSensorDataObjects.BarData;
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

        [Fact]
        public async void AddDoubleSensorValueTest()
        {
            var doubleSensorValue = new DoubleSensorValue()
            {
                DoubleValue = 123.123,
                Key = _databaseFixture.TestProduct.Key,
                Path = nameof(DoubleSensorValue),
                Description = $"{nameof(DoubleSensorValue)} {nameof(DoubleSensorValue.Description)}",
                Comment = $"{nameof(DoubleSensorValue)} {nameof(DoubleSensorValue.Comment)}",
                Time = DateTime.UtcNow,
            };

            _monitoringCore.AddSensorValue(doubleSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseFixture.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.DoubleSensor);
            SensorValuesTester.TestSensorDataFromCache(doubleSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(_databaseFixture.TestProduct.Name, doubleSensorValue.Path);
            SensorValuesTester.TestSensorHistoryDataFromDB(doubleSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(_databaseFixture.TestProduct.Name, doubleSensorValue.Path);
            SensorValuesTester.TestSensorInfoFromDB(doubleSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddStringSensorValueTest()
        {
            var stringSensorValue = new StringSensorValue()
            {
                StringValue = nameof(StringSensorValue.StringValue),
                Key = _databaseFixture.TestProduct.Key,
                Path = nameof(StringSensorValue),
                Description = $"{nameof(StringSensorValue)} {nameof(StringSensorValue.Description)}",
                Comment = $"{nameof(StringSensorValue)} {nameof(StringSensorValue.Comment)}",
                Time = DateTime.UtcNow,
            };

            _monitoringCore.AddSensorValue(stringSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseFixture.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.StringSensor);
            SensorValuesTester.TestSensorDataFromCache(stringSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(_databaseFixture.TestProduct.Name, stringSensorValue.Path);
            SensorValuesTester.TestSensorHistoryDataFromDB(stringSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(_databaseFixture.TestProduct.Name, stringSensorValue.Path);
            SensorValuesTester.TestSensorInfoFromDB(stringSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddIntBarSensorValueTest()
        {
            var intBarSensorValue = new IntBarSensorValue()
            {
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                EndTime = DateTime.UtcNow.AddSeconds(10),
                Count = 10,
                LastValue = 3,
                Min = -3,
                Max = 7,
                Mean = 1,
                Percentiles = new List<PercentileValueInt>(2) { new PercentileValueInt() { Percentile = 73.69, Value = 23 }, new PercentileValueInt() { Percentile = 6, Value = 7 } },
                Key = _databaseFixture.TestProduct.Key,
                Path = nameof(IntBarSensorValue),
                Description = $"{nameof(IntBarSensorValue)} {nameof(IntBarSensorValue.Description)}",
                Comment = $"{nameof(IntBarSensorValue)} {nameof(IntBarSensorValue.Comment)}",
                Time = DateTime.UtcNow,
            };

            _monitoringCore.AddSensorValue(intBarSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseFixture.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.IntegerBarSensor);
            SensorValuesTester.TestSensorDataFromCache(intBarSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(_databaseFixture.TestProduct.Name, intBarSensorValue.Path);
            SensorValuesTester.TestSensorHistoryDataFromDB(intBarSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(_databaseFixture.TestProduct.Name, intBarSensorValue.Path);
            SensorValuesTester.TestSensorInfoFromDB(intBarSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddDoubleBarSensorValueTest()
        {
            var doubleBarSensorValue = new DoubleBarSensorValue()
            {
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                EndTime = DateTime.UtcNow.AddSeconds(10),
                Count = 10,
                LastValue = 3.01,
                Min = -3.45,
                Max = 7.33,
                Mean = 1.09,
                Percentiles = new List<PercentileValueDouble>(2) { new PercentileValueDouble() { Percentile = 123.123, Value = 23.23 }, new PercentileValueDouble() { Percentile = 2, Value = 5 } },
                Key = _databaseFixture.TestProduct.Key,
                Path = nameof(DoubleBarSensorValue),
                Description = $"{nameof(DoubleBarSensorValue)} {nameof(DoubleBarSensorValue.Description)}",
                Comment = $"{nameof(DoubleBarSensorValue)} {nameof(DoubleBarSensorValue.Comment)}",
                Time = DateTime.UtcNow,
            };

            _monitoringCore.AddSensorValue(doubleBarSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseFixture.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.DoubleBarSensor);
            SensorValuesTester.TestSensorDataFromCache(doubleBarSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(_databaseFixture.TestProduct.Name, doubleBarSensorValue.Path);
            SensorValuesTester.TestSensorHistoryDataFromDB(doubleBarSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(_databaseFixture.TestProduct.Name, doubleBarSensorValue.Path);
            SensorValuesTester.TestSensorInfoFromDB(doubleBarSensorValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddFileSensorBytesValueTest()
        {
            var fileSensorBytesValue = new FileSensorBytesValue()
            {
                Extension = "csv",
                FileContent = new byte[] { 125, 30, 98 },
                FileName = nameof(FileSensorBytesValue),
                Key = _databaseFixture.TestProduct.Key,
                Path = nameof(FileSensorBytesValue),
                Description = $"{nameof(FileSensorBytesValue)} {nameof(FileSensorBytesValue.Description)}",
                Comment = $"{nameof(FileSensorBytesValue)} {nameof(FileSensorBytesValue.Comment)}",
                Time = DateTime.UtcNow,
            };

            _monitoringCore.AddSensorValue(fileSensorBytesValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseFixture.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.FileSensorBytes);
            SensorValuesTester.TestSensorDataFromCache(fileSensorBytesValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(_databaseFixture.TestProduct.Name, fileSensorBytesValue.Path);
            SensorValuesTester.TestSensorHistoryDataFromDB(fileSensorBytesValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(_databaseFixture.TestProduct.Name, fileSensorBytesValue.Path);
            SensorValuesTester.TestSensorInfoFromDB(fileSensorBytesValue, sensorInfoFromDB);
        }

        [Fact]
        public async void AddFileSensorValueTest()
        {
            var fileSensorValue = new FileSensorValue()
            {
                Extension = "csv",
                FileContent = $"{nameof(FileSensorValue)} {nameof(FileSensorValue.FileContent)}",
                FileName = nameof(FileSensorValue),
                Key = _databaseFixture.TestProduct.Key,
                Path = nameof(FileSensorValue),
                Description = $"{nameof(FileSensorValue)} {nameof(FileSensorValue.Description)}",
                Comment = $"{nameof(FileSensorValue)} {nameof(FileSensorValue.Comment)}",
                Time = DateTime.UtcNow,
            };

            _monitoringCore.AddSensorValue(fileSensorValue);

            await Task.Delay(100);

            var sensorDataFromCache =
                _valuesCache.GetValues(new List<string>(1) { _databaseFixture.TestProduct.Name })
                            ?.FirstOrDefault(s => s.SensorType == HSMSensorDataObjects.SensorType.FileSensor);
            SensorValuesTester.TestSensorDataFromCache(fileSensorValue, sensorDataFromCache);

            var sensorHistoryDataFromDB = _databaseFixture.DatabaseAdapter.GetOneValueSensorValue(_databaseFixture.TestProduct.Name, fileSensorValue.Path);
            SensorValuesTester.TestSensorHistoryDataFromDB(fileSensorValue, sensorHistoryDataFromDB);

            var sensorInfoFromDB = _databaseFixture.DatabaseAdapter.GetSensorInfo(_databaseFixture.TestProduct.Name, fileSensorValue.Path);
            SensorValuesTester.TestSensorInfoFromDB(fileSensorValue, sensorInfoFromDB);
        }
    }
}
