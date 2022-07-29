using HSMSensorDataObjects;
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
            _sensorValuesFactory = fixture.ApiSensorValuesFactory;

            _timeCollected = DateTime.UtcNow;
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
