using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public sealed class TreeValuesCacheFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TreeValuesCacheTests);


        internal override void InitializeDatabase(IDatabaseCore dbCore)
        {
            var (products, sensors, sensorValues) = GenerateTestData();

            foreach (var product in products)
                dbCore.AddProduct(product);

            foreach (var sensor in sensors)
                dbCore.AddSensor(sensor);

            foreach (var sensorValue in sensorValues)
                dbCore.AddSensorValue(sensorValue);
        }

        private static (List<ProductEntity>, List<SensorEntity>, List<SensorValueEntity>) GenerateTestData()
        {
            var products = new List<ProductEntity>(1 << 3);
            var sensors = new List<SensorEntity>(1 << 3);
            var sensorValues = new List<SensorValueEntity>(1 << 3);

            for (int i = 0; i < 2; ++i)
            {
                var product = EntitiesFactory.BuildProductEntity($"product{i}", null);

                for (int j = 0; j < 2; j++)
                {
                    var subProduct = EntitiesFactory.BuildProductEntity($"subProduct{j}_{product.DisplayName}", product.Id);

                    var sensor = new SensorEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductId = product.Id,
                        DisplayName = $"sensor{j}",
                        Type = (byte)SensorType.Boolean,
                    };
                    var boolValue = new BooleanValue()
                    {
                        Time = DateTime.UtcNow,
                        Status = SensorStatus.Warning,
                        Comment = "sensorValue",
                        Value = true,
                    };
                    var sensorValue = new SensorValueEntity()
                    {
                        SensorId = sensor.Id,
                        ReceivingTime = boolValue.ReceivingTime.Ticks,
                        Value = boolValue,
                    };

                    var subSubProduct = EntitiesFactory.BuildProductEntity($"subSubProduct", subProduct.Id);

                    var sensorForSubSubProduct = new SensorEntity()
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductId = subSubProduct.Id,
                        DisplayName = "sensor",
                        Type = (int)SensorType.Integer,
                    };
                    var intValue = new IntegerValue()
                    {
                        Time = DateTime.UtcNow,
                        Status = SensorStatus.Ok,
                        Comment = "sensorValue1",
                        Value = 12345,
                    };
                    var sensorForSubSubProductValue = new SensorValueEntity()
                    {
                        SensorId = sensorForSubSubProduct.Id,
                        ReceivingTime = intValue.ReceivingTime.Ticks,
                        Value = intValue,
                    };

                    products.Add(subProduct);
                    products.Add(subSubProduct);

                    sensors.Add(sensor);
                    sensors.Add(sensorForSubSubProduct);

                    sensorValues.Add(sensorValue);
                    sensorValues.Add(sensorForSubSubProductValue);
                }

                products.Add(product);
            }

            return (products, sensors, sensorValues);
        }
    }
}
