using HSMSensorDataObjects;
using HSMServer.Core.Converters;
using HSMServer.Core.Tests.Infrastructure;
using System;
using System.Text.Json;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorValuesToDataEntityConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly DateTime _timeCollected;


        public SensorValuesToDataEntityConverterTests(EntitiesConverterFixture converterFixture)
        {
            _sensorValuesFactory = converterFixture.SensorValuesFactory;

            _timeCollected = DateTime.UtcNow;
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "Simple")]
        public void SensorValueToSensorDataEntityConverterTest(SensorType type)
        {
            var sensorValue = _sensorValuesFactory.BuildSensorValue(type);

            var dataEntity = sensorValue.Convert(_timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(sensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        [Trait("Category", "BarSensorValue without EndTime")]
        public void IntBarSensorValueToSensorDataEntity_WithoutEndTime_ConverterTest()
        {
            DateTime sensorValueEndTime = DateTime.MinValue;

            var intBarSensorValue = _sensorValuesFactory.BuildIntBarSensorValue();
            intBarSensorValue.EndTime = sensorValueEndTime;

            var dataEntity = intBarSensorValue.Convert(_timeCollected, SensorStatus.Ok);

            Assert.DoesNotContain(JsonSerializer.Serialize(sensorValueEndTime), dataEntity.TypedData);
        }

        [Fact]
        [Trait("Category", "BarSensorValue without EndTime")]
        public void DoubleBarSensorValueToSensorDataEntityConverter_WithoutEndTime_Test()
        {
            DateTime sensorValueEndTime = DateTime.MinValue;

            var doubleBarSensorValue = _sensorValuesFactory.BuildDoubleBarSensorValue();
            doubleBarSensorValue.EndTime = sensorValueEndTime;

            var dataEntity = doubleBarSensorValue.Convert(_timeCollected, SensorStatus.Ok);

            Assert.DoesNotContain(JsonSerializer.Serialize(sensorValueEndTime), dataEntity.TypedData);
        }

        [Fact]
        [Trait("Category", "Validation result")]
        public void SensorValueToSensorDataEntityConverter_ValidationStatusError_Test()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            var dataEntity = fileSensorValue.Convert(_timeCollected, SensorStatus.Error);

            Assert.Equal((byte)SensorStatus.Error, dataEntity.Status);
        }

        [Fact]
        [Trait("Category", "Validation result")]
        public void SensorValueToSensorDataEntityConverter_SensorValueStatusError_Test()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorBytesValue();
            fileSensorValue.Status = SensorStatus.Error;

            var dataEntity = fileSensorValue.Convert(_timeCollected, SensorStatus.Warning);

            Assert.Equal((byte)SensorStatus.Error, dataEntity.Status);
        }

        [Fact]
        [Trait("Category", "Validation result")]
        public void SensorValueToSensorDataEntityConverter_ValidationStatusWarning_Test()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            var dataEntity = fileSensorValue.Convert(_timeCollected, SensorStatus.Warning);

            Assert.Equal((byte)SensorStatus.Warning, dataEntity.Status);
        }

        [Fact]
        [Trait("Category", "Validation result")]
        public void SensorValueToSensorDataEntityConverter_SensorValueStatusWarning_Test()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorBytesValue();
            fileSensorValue.Status = SensorStatus.Warning;

            var dataEntity = fileSensorValue.Convert(_timeCollected, SensorStatus.Unknown);

            Assert.Equal((byte)SensorStatus.Warning, dataEntity.Status);
        }
    }
}
