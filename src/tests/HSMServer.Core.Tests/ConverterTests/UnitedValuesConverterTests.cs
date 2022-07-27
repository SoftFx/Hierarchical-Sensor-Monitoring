﻿using HSMSensorDataObjects;
using HSMServer.Core.Converters;
using HSMServer.Core.Tests.Infrastructure;
using System;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class UnitedValuesConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private readonly ApiSensorValuesFactory _sensorValuesFactory;
        private readonly DateTime _timeCollected;

        public UnitedValuesConverterTests(EntitiesConverterFixture fixture)
        {
            _sensorValuesFactory = fixture.SensorValuesFactory;

            _timeCollected = DateTime.UtcNow;
        }


        //[Theory]
        //[InlineData(SensorType.BooleanSensor)]
        //[InlineData(SensorType.IntSensor)]
        //[InlineData(SensorType.DoubleSensor)]
        //[InlineData(SensorType.StringSensor)]
        //[InlineData(SensorType.IntegerBarSensor)]
        //[InlineData(SensorType.DoubleBarSensor)]
        //[Trait("Category", "to SensorDataEntity")]
        //public void UnitedValueToSensorDataEntityTest(SensorType sensorType)
        //{
        //    var unitedValue = _sensorValuesFactory.BuildUnitedSensorValue(sensorType);

        //    var sensorDataEntity = unitedValue.Convert(_timeCollected, SensorStatus.Ok);

        //    SensorValuesTester.TestSensorDataEntity(unitedValue, sensorDataEntity, _timeCollected);
        //}

        //[Theory]
        //[InlineData(SensorType.FileSensor)]
        //[InlineData(SensorType.FileSensorBytes)]
        //[Trait("Category", "to SensorDataEntity")]
        //public void UnitedValueToSensorDataEntity_WithoutSpecificFields_Test(SensorType sensorType)
        //{
        //    var unitedValue = _sensorValuesFactory.BuildUnitedSensorValue(sensorType);

        //    var sensorDataEntity = unitedValue.Convert(_timeCollected, SensorStatus.Ok);

        //    Assert.Equal(string.Empty, sensorDataEntity.TypedData);
        //}


        [Theory]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "to BarSensorValue")]
        public void UnitedValueToBarSensorValueTest(SensorType sensorType)
        {
            var unitedValue = _sensorValuesFactory.BuildUnitedSensorValue(sensorType);

            var barSensorValue = unitedValue.Convert();

            SensorValuesTester.TestBarSensorFromUnitedSensor(unitedValue, barSensorValue);
        }

        [Fact]
        [Trait("Category", "to BarSensorValue")]
        public void UnitedValueToBarSensorValue_Null_Test()
        {
            const SensorType sensorType = SensorType.BooleanSensor;

            var unitedValue = _sensorValuesFactory.BuildUnitedSensorValue(sensorType);

            var barSensorValue = unitedValue.Convert();

            Assert.Null(barSensorValue);
        }
    }
}
