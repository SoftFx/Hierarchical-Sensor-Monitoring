using System;
using System.Text.Json;
using HSMSensorDataObjects;
using HSMServer.Core.Converters;
using HSMServer.Core.Tests.MonitoringDataReceiverTests;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorValuesToDataEntityConverterTests : IClassFixture<SensorValuesToDataEntityConverterFixture>
    {
        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly DateTime _timeCollected;


        public SensorValuesToDataEntityConverterTests(SensorValuesToDataEntityConverterFixture converterFixture)
        {
            _sensorValuesFactory = converterFixture.SensorValuesFactory;

            _timeCollected = DateTime.UtcNow;
        }


        [Fact]
        [Trait("Category", "Simple")]
        public void BoolSensorValueToSensorDataEntityConverterTest()
        {
            var boolSensorValue = _sensorValuesFactory.BuildBoolSensorValue();

            var dataEntity = boolSensorValue.Convert(_timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(boolSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void IntSensorValueToSensorDataEntityConverterTest()
        {
            var intSensorValue = _sensorValuesFactory.BuildIntSensorValue();

            var dataEntity = intSensorValue.Convert(_timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(intSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void DoubleSensorValueToSensorDataEntityConverterTest()
        {
            var doubleSensorValue = _sensorValuesFactory.BuildDoubleSensorValue();

            var dataEntity = doubleSensorValue.Convert(_timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(doubleSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void StringSensorValueToSensorDataEntityConverterTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();

            var dataEntity = stringSensorValue.Convert(_timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(stringSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void IntBarSensorValueToSensorDataEntityConverterTest()
        {
            var intBarSensorValue = _sensorValuesFactory.BuildIntBarSensorValue();

            var dataEntity = intBarSensorValue.Convert(_timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(intBarSensorValue, dataEntity, _timeCollected);
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
        [Trait("Category", "Simple")]
        public void DoubleBarSensorValueToSensorDataEntityConverterTest()
        {
            var doubleBarSensorValue = _sensorValuesFactory.BuildDoubleBarSensorValue();

            var dataEntity = doubleBarSensorValue.Convert(_timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(doubleBarSensorValue, dataEntity, _timeCollected);
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
        [Trait("Category", "Simple")]
        public void FileSensorBytesValueToSensorDataEntityConverterTest()
        {
            var fileSensorBytesValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            var dataEntity = fileSensorBytesValue.Convert(_timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(fileSensorBytesValue, dataEntity, _timeCollected);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void FileSensorValueToSensorDataEntityConverterTest()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();

            var dataEntity = fileSensorValue.Convert(_timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(fileSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        [Trait("Category", "Validation result")]
        public void SensorValueToSensorDataEntityConverter_ValidationStatusError_Test()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();

            var dataEntity = fileSensorValue.Convert(_timeCollected, SensorStatus.Error);

            Assert.Equal((byte)SensorStatus.Error, dataEntity.Status);
        }

        [Fact]
        [Trait("Category", "Validation result")]
        public void SensorValueToSensorDataEntityConverter_SensorValueStatusError_Test()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();
            fileSensorValue.Status = SensorStatus.Error;

            var dataEntity = fileSensorValue.Convert(_timeCollected, SensorStatus.Warning);

            Assert.Equal((byte)SensorStatus.Error, dataEntity.Status);
        }

        [Fact]
        [Trait("Category", "Validation result")]
        public void SensorValueToSensorDataEntityConverter_ValidationStatusWarning_Test()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();

            var dataEntity = fileSensorValue.Convert(_timeCollected, SensorStatus.Warning);

            Assert.Equal((byte)SensorStatus.Warning, dataEntity.Status);
        }

        [Fact]
        [Trait("Category", "Validation result")]
        public void SensorValueToSensorDataEntityConverter_SensorValueStatusWarning_Test()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();
            fileSensorValue.Status = SensorStatus.Warning;

            var dataEntity = fileSensorValue.Convert(_timeCollected, SensorStatus.Unknown);

            Assert.Equal((byte)SensorStatus.Warning, dataEntity.Status);
        }
    }
}
