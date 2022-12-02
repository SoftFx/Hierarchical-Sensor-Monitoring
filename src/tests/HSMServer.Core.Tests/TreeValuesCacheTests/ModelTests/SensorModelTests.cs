using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using System;
using Xunit;
using SensorModelFactory = HSMServer.Core.Tests.Infrastructure.SensorModelFactory;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.ModelTests
{
    public class SensorModelTests
    {
        [Fact]
        [Trait("Category", "SensorModel creation")]
        public void SensorModelCreationTest()
        {
            var sensorEntity = EntitiesFactory.BuildSensorEntity();

            var sensor = SensorModelFactory.Build(sensorEntity);

            Assert.NotEqual(Guid.Empty, sensor.Id);
            Assert.NotEqual(DateTime.MinValue, sensor.CreationDate);
            ModelsTester.TestSensorModel(sensorEntity, sensor);
        }

        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "Add sensor value")]
        public void SensorModel_AddValue_Test(SensorType type)
        {
            var sensorEntity = EntitiesFactory.BuildSensorEntity(type: (byte)type);
            var sensor = SensorModelFactory.Build(sensorEntity);

            var sensorValue = SensorValuesFactory.BuildSensorValue(type);

            sensor.TryAddValue(sensorValue, out var cachedValue);

            Assert.True(sensor.HasData);
            Assert.NotEqual(DateTime.MinValue, sensor.LastUpdateTime);
            ModelsTester.AssertModels(sensorValue, sensor.LastValue);

            if (sensor is IBarSensor barSensor)
            {
                Assert.Null(cachedValue);
                ModelsTester.AssertModels(sensorValue, barSensor.LocalLastValue);
            }
            else
                ModelsTester.AssertModels(sensorValue, cachedValue);
        }

        [Fact]
        [Trait("Category", "SensorModel updating")]
        public void SensorModel_Update_Test()
        {
            var updating = SensorModelFactory.BuildSensorUpdate();
            var sensor = SensorModelFactory.Build(EntitiesFactory.BuildSensorEntity());

            sensor.Update(updating);

            ModelsTester.TestSensorModel(updating, sensor);
        }

        [Fact]
        [Trait("Category", "SensorModel to entity")]
        public void SensorModelToSensorEntityTest()
        {
            var product = new ProductModel("product");
            var entity = EntitiesFactory.BuildSensorEntity();

            var sensor = SensorModelFactory.Build(entity);
            product.AddSensor(sensor);

            var sensorEntity = sensor.ToEntity();

            ModelsTester.TestSensorModel(sensorEntity, sensor);
        }
    }
}
