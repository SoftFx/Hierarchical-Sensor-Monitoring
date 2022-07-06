using HSMSensorDataObjects;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.SensorsDataValidation;
using HSMServer.Core.Tests.Infrastructure;
using System;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.ModelTests
{
    public class SensorModelTests
    {
        private readonly SensorValuesFactory _sensorValuesFactory = new(TestProductsManager.ProductName);


        [Fact]
        [Trait("Category", "SensorModel constructor")]
        public void SensorModelConstructor_SensorEntity_Test()
        {
            var sensorEntity = EntitiesFactory.BuildSensorEntity();
            var sensorDataEntity = EntitiesFactory.BuildSensorDataEntity(sensorEntity.Path, (byte)sensorEntity.SensorType);

            var sensor = new SensorModel(sensorEntity, sensorDataEntity);
            sensor.AddParent(new ProductModel(sensorEntity.ProductId, RandomGenerator.GetRandomString()));

            ModelsTester.TestSensorModel(sensorEntity, sensor);
            ModelsTester.TestSensorModel(sensorDataEntity, sensor);
        }

        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "SensorModel constructor")]
        public void SensorModelConstructor_SensorValue_Test(SensorType type)
        {
            var timeCollected = DateTime.UtcNow;
            var sensorValue = _sensorValuesFactory.BuildSensorValue(type);

            var sensor = new SensorModel(sensorValue, TestProductsManager.ProductName, timeCollected, new ValidationResult(sensorValue));

            ModelsTester.TestSensorModel(sensorValue, TestProductsManager.ProductName, timeCollected, sensor);
        }

        [Fact]
        [Trait("Category", "SensorModel constructor")]
        public void SensorModelConstructor_FileSensorBytes_Test()
        {
            var timeCollected = DateTime.UtcNow;
            var sensorValue = _sensorValuesFactory.BuildFileSensorBytesValue();
            int originalContentSize = sensorValue.FileContent.Length;

            var sensor = new SensorModel(sensorValue, TestProductsManager.ProductName, timeCollected, new ValidationResult(sensorValue));

            ModelsTester.TestSensorModel(sensorValue, TestProductsManager.ProductName, timeCollected, sensor);
            Assert.Equal(originalContentSize, sensor.OriginalFileSensorContentSize);
        }

        //[Theory]
        //[InlineData(SensorType.BooleanSensor)]
        //[InlineData(SensorType.IntSensor)]
        //[InlineData(SensorType.DoubleSensor)]
        //[InlineData(SensorType.StringSensor)]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[Trait("Category", "SensorModel constructor")]
        //public void SensorModelConstructor_UnitedSensor_Test(SensorType type)
        //{
        //    var timeCollected = DateTime.UtcNow;
        //    var sensorValue = _sensorValuesFactory.BuildUnitedSensorValue(type);

        //    var sensor = new SensorModel(sensorValue, TestProductsManager.ProductName, timeCollected, new ValidationResult(sensorValue));

        //    ModelsTester.TestSensorModel(sensorValue, TestProductsManager.ProductName, timeCollected, sensor);
        //}


        [Fact]
        [Trait("Category", "SensorModel updating")]
        public void SensorModel_Update_Test()
        {
            var updating = TreeValuesCacheTests.BuildSensorUpdate();
            var sensor = new SensorModel(EntitiesFactory.BuildSensorEntity(), null);

            sensor.Update(updating);

            ModelsTester.TestSensorModel(updating, sensor);
        }

        [Fact]
        [Trait("Category", "SensorModel updating")]
        public void SensorModel_UpdateData_Test()
        {
            var timeCollected = DateTime.UtcNow;
            var sensorValue = _sensorValuesFactory.BuildBoolSensorValue();

            var sensor = new SensorModel(sensorValue, TestProductsManager.ProductName, timeCollected, new ValidationResult(sensorValue));

            var updatedTimeCollected = DateTime.UtcNow.AddDays(1);
            var updatedSensorValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            sensor.UpdateData(updatedSensorValue, updatedTimeCollected, new ValidationResult(sensorValue));

            ModelsTester.TestSensorModelData(updatedSensorValue, updatedTimeCollected, sensor);
        }


        [Fact]
        [Trait("Category", "SensorModel to entity")]
        public void SensorModelToSensorEntityTest()
        {
            var entity = EntitiesFactory.BuildSensorEntity();

            var sensor = new SensorModel(entity, null);
            sensor.AddParent(new ProductModel(entity.ProductId, RandomGenerator.GetRandomString()));

            var sensorEntity = sensor.ToSensorEntity();

            ModelsTester.TestSensorModel(sensorEntity, sensor);
        }

        [Fact]
        [Trait("Category", "SensorModel to entity")]
        public void SensorModelToSensorDataEntityTest()
        {
            var entity = EntitiesFactory.BuildSensorEntity();
            var dataEntity = EntitiesFactory.BuildSensorDataEntity(entity.Path, (byte)entity.SensorType);

            var sensor = new SensorModel(entity, dataEntity);

            var sensorDataEntity = sensor.ToSensorDataEntity();

            ModelsTester.TestSensorModel(sensorDataEntity, sensor);
        }
    }
}
