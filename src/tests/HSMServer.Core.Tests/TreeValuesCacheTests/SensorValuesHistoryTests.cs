﻿using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.Extensions;
using Xunit;
using System.Threading;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.ApiObjectsConverters;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class SensorValuesHistoryTests : MonitoringCoreTestsBase<SensorValuesHistoryFixture>
    {
        public SensorValuesHistoryTests(SensorValuesHistoryFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.Rate)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "Getting sensor values history")]
        public async Task GetValues_Count_Test(SensorType type, int sensorsCount = 5)
        {
            const int sensorValuesCount = 1000;
            const int historyValuesCount = 101;

            var sensors = await AddAndGetSensorsWithAllSensorValues(sensorsCount, sensorValuesCount, type);

            foreach (var (sensor, sensorValues) in sensors)
            {
                sensorValues.Reverse();

                var expectedValues = type is SensorType.IntegerBar or SensorType.DoubleBar
                    ? sensorValues.Skip(1).Take(historyValuesCount).ToList() // skip last value because GetSensorValuesPage returns values only from db (not from cache)
                    : sensorValues.Take(historyValuesCount).ToList();

                var actualValues = await _valuesCache.GetSensorValuesPage(sensor.Id, DateTime.MinValue, DateTime.MaxValue, -historyValuesCount).Flatten();

                Assert.True(historyValuesCount >= actualValues.Count);
                Assert.Equal(expectedValues.Count, actualValues.Count);

                for (int i = 0; i < expectedValues.Count; ++i)
                    ModelsTester.AssertModels(expectedValues[i], actualValues[i], ["ReceivingTime","LastUpdateTime"]);
            }
        }

        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.Rate)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "Getting sensor values history")]
        public async Task GetValues_Period_Test(SensorType type, int sensorsCount = 5)
        {
            const int sensorValuesCount = 5000;

            var sensors = await AddAndGetSensorsWithAllSensorValues(sensorsCount, sensorValuesCount, type);

            var from = DateTime.UtcNow.AddMilliseconds(-100);
            var to = DateTime.UtcNow.AddMilliseconds(-50);

            foreach (var (sensor, sensorValues) in sensors)
            {
                sensorValues.Reverse();

                var expectedValues = sensorValues.Where(s => s.Time >= from && s.Time <= to).ToList();
                var actualValues = await _valuesCache.GetSensorValuesPage(sensor.Id, from, to, MaxHistoryCount).Flatten();

                Assert.Equal(expectedValues.Count, actualValues.Count);
                for (int i = 0; i < expectedValues.Count; ++i)
                    ModelsTester.AssertModels(expectedValues[i], actualValues[i], ["ReceivingTime", "LastUpdateTime"]);
            }
        }

        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.Rate)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "Getting sensor values history")]
        public async Task GetAllValuesTest(SensorType type, int sensorsCount = 2)
        {
            const int sensorValuesCount = 1000;

            var sensors = await AddAndGetSensorsWithAllSensorValues(sensorsCount, sensorValuesCount, type);

            await Task.Delay(100);

            var from = DateTime.MinValue;
            var to = DateTime.MaxValue;

            foreach (var (sensor, values) in sensors)
            {
                var expectedValues = values;

                expectedValues.Reverse();
                if (type is SensorType.IntegerBar or SensorType.DoubleBar)
                    expectedValues = expectedValues.Skip(1).ToList(); // skip last value because GetSensorValuesPage returns values only from db (not from cache)

                var actualValues = await _valuesCache.GetSensorValuesPage(sensor.Id, from, to, MaxHistoryCount).Flatten();

                Assert.Equal(expectedValues.Count, actualValues.Count);
                for (int i = 0; i < expectedValues.Count; ++i)
                    ModelsTester.AssertModels(expectedValues[i], actualValues[i], ["ReceivingTime", "LastUpdateTime"]);
            }
        }


        private async Task<Dictionary<SensorModelInfo, List<BaseValue>>> AddAndGetSensorsWithAllSensorValues(int sensorsCount, int sensorValuesCount, SensorType type)
        {
            var sensors = BuildSensorModelInfos(sensorsCount, type);

            var map = new Dictionary<SensorModelInfo, List<BaseValue>>(sensorsCount);
            foreach (var sensorInfo in sensors)
                map.Add(sensorInfo, new List<BaseValue>());

            var product = _valuesCache.GetProductByName(TestProductsManager.TestProductModel.DisplayName);

            var key = product.AccessKeys.Values.FirstOrDefault(x => x.State == KeyState.Active);

            for (int i = 0; i < sensorValuesCount; ++i)
            {
                var sensorInfo = sensors[RandomGenerator.GetRandomInt(min: 0, max: sensorsCount)];
                var value = SensorValuesFactory.BuildSensorValue(sensorInfo.Type, sensorInfo.Path);


                await _valuesCache.AddSensorValueAsync(key.Id, TestProductsManager.ProductId,  value);
                map[sensorInfo].Add(value.Convert());
            }

            var sens = _valuesCache.GetSensors();
            foreach (var sensor in map.Keys)
            {
                var result = sens.FirstOrDefault(x => x.Path == sensor.Path);
                sensor.Id = result.Id;
            }

            return map;
        }

        private List<SensorModelInfo> BuildSensorModelInfos(int sensorsCount, SensorType type)
        {
            var sensors = new List<SensorModelInfo>(sensorsCount);

            for (int i = 0; i < sensorsCount; ++i)
            {
                var sensorEntity = EntitiesFactory.BuildSensorEntity(parent: TestProductsManager.TestProduct.Id, type: (byte)type);

                var sensor = Infrastructure.SensorModelFactory.Build(sensorEntity);
                sensor.AddParent(_valuesCache.GetProduct(sensorEntity.ProductId.ToGuid()));

                var info = new SensorModelInfo()
                {
                    Id = sensor.Id,
                    Path = sensor.Path,
                    Type = sensor.Type,
                };

                sensors.Add(info);
            }

            return sensors;
        }


        public sealed class SensorModelInfo
        {
            public string Path { get; init; }

            public SensorType Type { get; init; }


            public Guid Id { get; set; } = Guid.NewGuid();
        }
    }
}
