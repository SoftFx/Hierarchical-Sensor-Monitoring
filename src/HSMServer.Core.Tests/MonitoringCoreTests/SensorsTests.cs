using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Products;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    public class SensorsTests : IClassFixture<SensorsFixture>
    {
        private readonly string _testProductName = DatabaseAdapterManager.ProductName;

        private readonly MonitoringCore _monitoringCore;
        private readonly ProductManager _productManager;

        private readonly DatabaseAdapterManager _databaseAdapterManager;
        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly SensorValuesTester _sensorValuesTester;


        public SensorsTests(SensorsFixture fixture)
        {
            _databaseAdapterManager = new DatabaseAdapterManager(fixture.DatabasePath);
            _databaseAdapterManager.AddTestProduct();
            fixture.CreatedDatabases.Add(_databaseAdapterManager);

            _sensorValuesFactory = new SensorValuesFactory(_databaseAdapterManager);
            _sensorValuesTester = new SensorValuesTester(_databaseAdapterManager);

            var userManager = new Mock<IUserManager>();

            var barStorage = new Mock<IBarSensorsStorage>();
            var configurationProvider = new Mock<IConfigurationProvider>();
            var valuesCache = new Mock<IValuesCache>();

            var productManagerLogger = CommonMoqs.CreateNullLogger<ProductManager>();
            _productManager = new ProductManager(_databaseAdapterManager.DatabaseAdapter, productManagerLogger);

            var monitoringLogger = CommonMoqs.CreateNullLogger<MonitoringCore>();
            _monitoringCore = new MonitoringCore(
                _databaseAdapterManager.DatabaseAdapter,
                userManager.Object,
                barStorage.Object,
                _productManager,
                configurationProvider.Object,
                valuesCache.Object,
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
                var sensorValue = _sensorValuesFactory.BuildRandomSensorValue();
                var sensorValuePath = sensorValue.Path;

                _monitoringCore.AddSensor(_testProductName, sensorValue);

                Assert.True(_monitoringCore.IsSensorRegistered(_testProductName, sensorValuePath));
                _sensorValuesTester.TestSensorInfoFromDB(sensorValue, _databaseAdapterManager.DatabaseAdapter.GetSensorInfo(_testProductName, sensorValuePath));
                _sensorValuesTester.TestSensorInfoFromDB(sensorValue, _monitoringCore.GetSensorInfo(_testProductName, sensorValuePath));
                _sensorValuesTester.TestSensorInfoFromDB(sensorValue, _monitoringCore.GetProductSensors(_testProductName).FirstOrDefault(s => s.Path == sensorValuePath));
            }
        }

        [Fact]
        [Trait("Category", "Add Sensor (non-existing product)")]
        public void AddSensor_NonExistingProduct_Test()
        {
            var productName = RandomValuesGenerator.GetRandomString();

            var sensorValue = _sensorValuesFactory.BuildRandomSensorValue();
            var sensorValuePath = sensorValue.Path;

            _monitoringCore.AddSensor(productName, sensorValue);

            FullTestNonExistingSensorInfo(_testProductName,
                                          sensorValue.Path,
                                          _monitoringCore.IsSensorRegistered,
                                          _monitoringCore.GetSensorInfo,
                                          _monitoringCore.GetProductSensors,
                                          _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }

        [Fact]
        [Trait("Category", "Update Sensor (non-existing product)")]
        public void UpdateSensorInfo_NonExisting_Test()
        {
            var sensorInfo = SensorInfoFactory.BuildSensorInfo(_testProductName, (byte)RandomValuesGenerator.GetRandomInt(min: 0, max: 8));

            _monitoringCore.UpdateSensorInfo(sensorInfo);

            FullTestNonExistingSensorInfo(_testProductName,
                                          sensorInfo.Path,
                                          _monitoringCore.IsSensorRegistered,
                                          _monitoringCore.GetSensorInfo,
                                          _monitoringCore.GetProductSensors,
                                          _databaseAdapterManager.DatabaseAdapter.GetSensorInfo);
        }


        private static void FullTestNonExistingSensorInfo(string productName, string sensorPath, Func<string, string, bool> isSensorReqistered,
            Func<string, string, SensorInfo> getSensorInfo, Func<string, ICollection<SensorInfo>> getProductSensors, Func<string, string, SensorInfo> getSensorInfoFromDB)
        {
            Assert.False(isSensorReqistered(productName, sensorPath));
            Assert.Null(getSensorInfo(productName, sensorPath));
            Assert.Null(getProductSensors(productName)?.FirstOrDefault(s => s.Path == sensorPath));
            Assert.Null(getSensorInfoFromDB(productName, sensorPath));
        }
    }
}
