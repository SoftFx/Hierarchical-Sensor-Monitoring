﻿using System;
using HSMSensorDataObjects;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class UnitedValuesConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private readonly Converter _converter;
        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly SensorValuesTester _sensorValuesTester;

        private readonly DateTime _timeCollected;
        private readonly string _productName = EntitiesConverterFixture.ProductKey;

        public UnitedValuesConverterTests(EntitiesConverterFixture fixture)
        {
            _converter = new Converter(CommonMoqs.CreateNullLogger<Converter>());
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

            var sensorData = _converter.ConvertUnitedValue(unitedValue, _productName, _timeCollected, transactionType);

            _sensorValuesTester.TestSensorData(unitedValue, sensorData, _timeCollected, transactionType);
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

            var sensorDataEntity = _converter.ConvertUnitedValueToDatabase(unitedValue, _timeCollected, SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(unitedValue, sensorDataEntity, _timeCollected);
        }
    }
}
