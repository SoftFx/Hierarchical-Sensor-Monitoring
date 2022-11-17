using HSMCommon.Constants;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class SensorValuesHistoryTests : MonitoringCoreTestsBase<SensorValuesHistoryFixture>
    {
        private readonly TreeValuesCache _valuesCache;


        public SensorValuesHistoryTests(SensorValuesHistoryFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _valuesCache = new TreeValuesCache(_databaseCoreManager.DatabaseCore, _userManager, _updatesQueue);
        }


        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "Getting sensor values history")]
        public void GetValues_Count_Test(SensorType type, int sensorsCount = 5)
        {
            const int sensorValuesCount = 1000;
            const int historyValuesCount = 101;

            var sensors = AddAndGetSensorsWithAllSensorValues(sensorsCount, sensorValuesCount, type);

            foreach (var (sensor, sensorValues) in sensors)
            {
                sensorValues.Reverse();

                var expectedValues = sensorValues.Take(historyValuesCount).ToList();
                var actualValues = _valuesCache.GetSensorValues(sensor.Id, historyValuesCount);

                Assert.True(historyValuesCount >= actualValues.Count);
                Assert.Equal(expectedValues.Count, actualValues.Count);

                for (int i = 0; i < expectedValues.Count; ++i)
                    ModelsTester.AssertModels(expectedValues[i], actualValues[i]);
            }
        }

        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "Getting sensor values history")]
        public void GetValues_Period_Test(SensorType type, int sensorsCount = 5)
        {
            const int sensorValuesCount = 5000;

            var sensors = AddAndGetSensorsWithAllSensorValues(sensorsCount, sensorValuesCount, type);

            var from = DateTime.UtcNow.AddMilliseconds(-100);
            var to = DateTime.UtcNow.AddMilliseconds(-50);

            foreach (var (sensor, sensorValues) in sensors)
            {
                sensorValues.Reverse();

                var expectedValues = sensorValues.Where(s => s.ReceivingTime >= from && s.ReceivingTime <= to).ToList();
                var actualValues = _valuesCache.GetSensorValues(sensor.Id, from, to);

                Assert.Equal(expectedValues.Count, actualValues.Count);
                for (int i = 0; i < expectedValues.Count; ++i)
                    ModelsTester.AssertModels(expectedValues[i], actualValues[i]);
            }
        }

        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "Getting sensor values history")]
        public void GetAllValuesTest(SensorType type, int sensorsCount = 2)
        {
            const int sensorValuesCount = 1000;

            var sensors = AddAndGetSensorsWithAllSensorValues(sensorsCount, sensorValuesCount, type);

            var from = DateTime.MinValue;
            var to = DateTime.MaxValue;

            foreach (var (sensor, expectedValues) in sensors)
            {
                expectedValues.Reverse();

                var actualValues = _valuesCache.GetSensorValues(sensor.Id, from, to);

                Assert.Equal(expectedValues.Count, actualValues.Count);
                for (int i = 0; i < expectedValues.Count; ++i)
                    ModelsTester.AssertModels(expectedValues[i], actualValues[i]);
            }
        }


        private Dictionary<SensorModelInfo, List<BaseValue>> AddAndGetSensorsWithAllSensorValues(int sensorsCount, int sensorValuesCount, SensorType type)
        {
            var sensors = BuildSensorModelInfos(sensorsCount, type);

            var map = new Dictionary<SensorModelInfo, List<BaseValue>>(sensorsCount);
            foreach (var sensorInfo in sensors)
                map.Add(sensorInfo, new List<BaseValue>());

            for (int i = 0; i < sensorValuesCount; ++i)
            {
                var sensorInfo = sensors[RandomGenerator.GetRandomInt(min: 0, max: sensorsCount)];
                var storeInfo = new StoreInfo(CommonConstants.SelfMonitoringProductKey, sensorInfo.Path)
                {
                    BaseValue = SensorValuesFactory.BuildSensorValue(sensorInfo.Type),
                };

                void ChangeSensorHandler(BaseSensorModel sensorModel, TransactionType type)
                {
                    if (type == TransactionType.Add)
                        sensorInfo.Id = sensorModel.Id;
                }

                _valuesCache.ChangeSensorEvent += ChangeSensorHandler;
                _valuesCache.AddNewSensorValue(storeInfo);
                _valuesCache.ChangeSensorEvent -= ChangeSensorHandler;

                map[sensorInfo].Add(storeInfo.BaseValue);
            }

            return map;
        }

        private List<SensorModelInfo> BuildSensorModelInfos(int sensorsCount, SensorType type)
        {
            var sensors = new List<SensorModelInfo>(sensorsCount);
            for (int i = 0; i < sensorsCount; ++i)
            {
                var sensorEntity = EntitiesFactory.BuildSensorEntity(parent: CommonConstants.SelfMonitoringProductKey, type: (byte)type);

                var sensor = Infrastructure.SensorModelFactory.Build(sensorEntity);
                sensor.ParentProduct = _valuesCache.GetProduct(sensorEntity.ProductId);
                sensor.BuildProductNameAndPath();

                var info = new SensorModelInfo()
                {
                    Path = sensor.Path,
                    Type = sensor.Type,
                };

                sensors.Add(info);
            }

            return sensors;
        }


        private sealed class SensorModelInfo
        {
            public Guid Id { get; set; }

            public string Path { get; init; }

            public SensorType Type { get; init; }
        }
    }
}
