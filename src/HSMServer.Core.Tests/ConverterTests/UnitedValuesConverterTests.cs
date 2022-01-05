﻿using System;
using HSMSensorDataObjects;
using HSMServer.Core.Converters;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class UnitedValuesConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private const string ProductName = EntitiesConverterFixture.ProductKey;

        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly SensorValuesTester _sensorValuesTester;
        private readonly DateTime _timeCollected;

        public UnitedValuesConverterTests(EntitiesConverterFixture fixture)
        {
            _sensorValuesFactory = fixture.SensorValuesFactory;
            _sensorValuesTester = fixture.SensorValuesTester;

            _timeCollected = DateTime.UtcNow;
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor, TransactionType.Add)]
        [InlineData(SensorType.IntSensor, TransactionType.Delete)]
        [InlineData(SensorType.DoubleSensor, TransactionType.Unknown)]
        [InlineData(SensorType.StringSensor, TransactionType.Update)]
        [InlineData(SensorType.IntegerBarSensor, TransactionType.UpdateTree)]
        [InlineData(SensorType.DoubleBarSensor, TransactionType.Add)]
        [Trait("Category", "to SensorData")]
        public void UnitedValueToSensorDataTest(SensorType sensorType, TransactionType transactionType)
        {
            var unitedValue = _sensorValuesFactory.BuildUnitedSensorValue(sensorType);

            var sensorData = unitedValue.Convert(ProductName, _timeCollected, transactionType);

            _sensorValuesTester.TestSensorData(unitedValue, sensorData, _timeCollected, transactionType);
        }

        [Theory]
        [InlineData(SensorType.FileSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "to SensorData")]
        public void UnitedValueToSensorData_WithoutSpecificFields_Test(SensorType sensorType)
        {
            var unitedValue = _sensorValuesFactory.BuildUnitedSensorValue(sensorType);

            var sensorData = unitedValue.Convert(ProductName, _timeCollected, TransactionType.Unknown);

            Assert.Equal(string.Empty, sensorData.StringValue);
            Assert.Equal(string.Empty, sensorData.ShortStringValue);
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "to SensorDataEntity")]
        public void UnitedValueToSensorDataEntityTest(SensorType sensorType)
        {
            var unitedValue = _sensorValuesFactory.BuildUnitedSensorValue(sensorType);

            var sensorDataEntity = unitedValue.Convert(_timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(unitedValue, sensorDataEntity, _timeCollected);
        }

        [Theory]
        [InlineData(SensorType.FileSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "to SensorDataEntity")]
        public void UnitedValueToSensorDataEntity_WithoutSpecificFields_Test(SensorType sensorType)
        {
            var unitedValue = _sensorValuesFactory.BuildUnitedSensorValue(sensorType);

            var sensorDataEntity = unitedValue.Convert(_timeCollected, SensorStatus.Ok);

            Assert.Equal(string.Empty, sensorDataEntity.TypedData);
        }


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
