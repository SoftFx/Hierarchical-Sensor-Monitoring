using System.Text.Json;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Converters;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class CommonSensorValuesToSensorValuesConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private readonly SensorValuesFactory _sensorValuesFactory;


        public CommonSensorValuesToSensorValuesConverterTests(EntitiesConverterFixture fixture) =>
            _sensorValuesFactory = fixture.SensorValuesFactory;


        [Fact]
        public void CommonSensorValueToBoolSensorValueTest()
        {
            var expected = _sensorValuesFactory.BuildBoolSensorValue();

            var commonSensorValue = new CommonSensorValue()
            {
                SensorType = SensorType.BooleanSensor,
                TypedValue = JsonSerializer.Serialize(expected),
            };

            var actual = commonSensorValue.Convert<BoolSensorValue>();

            Assert.Equal(expected.BoolValue, actual.BoolValue);
            TestTwoSensorValues(expected, actual);
        }

        [Fact]
        public void CommonSensorValueToIntSensorValueTest()
        {
            var expected = _sensorValuesFactory.BuildIntSensorValue();

            var commonSensorValue = new CommonSensorValue()
            {
                SensorType = SensorType.IntSensor,
                TypedValue = JsonSerializer.Serialize(expected),
            };

            var actual = commonSensorValue.Convert<IntSensorValue>();

            Assert.Equal(expected.IntValue, actual.IntValue);
            TestTwoSensorValues(expected, actual);
        }

        [Fact]
        public void CommonSensorValueToDoubleSensorValueTest()
        {
            var expected = _sensorValuesFactory.BuildDoubleSensorValue();

            var commonSensorValue = new CommonSensorValue()
            {
                SensorType = SensorType.DoubleSensor,
                TypedValue = JsonSerializer.Serialize(expected),
            };

            var actual = commonSensorValue.Convert<DoubleSensorValue>();

            Assert.Equal(expected.DoubleValue, actual.DoubleValue);
            TestTwoSensorValues(expected, actual);
        }

        [Fact]
        public void CommonSensorValueToStringSensorValueTest()
        {
            var expected = _sensorValuesFactory.BuildStringSensorValue();

            var commonSensorValue = new CommonSensorValue()
            {
                SensorType = SensorType.StringSensor,
                TypedValue = JsonSerializer.Serialize(expected),
            };

            var actual = commonSensorValue.Convert<StringSensorValue>();

            Assert.Equal(expected.StringValue, actual.StringValue);
            TestTwoSensorValues(expected, actual);
        }

        [Fact]
        public void CommonSensorValueToIntBarSensorValueTest()
        {
            var expected = _sensorValuesFactory.BuildIntBarSensorValue();

            var commonSensorValue = new CommonSensorValue()
            {
                SensorType = SensorType.IntegerBarSensor,
                TypedValue = JsonSerializer.Serialize(expected),
            };

            var actual = commonSensorValue.Convert<IntBarSensorValue>();

            Assert.Equal(expected.Max, actual.Max);
            Assert.Equal(expected.Mean, actual.Mean);
            Assert.Equal(expected.Min, actual.Min);
            Assert.Equal(expected.LastValue, actual.LastValue);
            TestTwoBarSensorValues(expected, actual);
        }

        [Fact]
        public void CommonSensorValueToDoubleBarSensorValueTest()
        {
            var expected = _sensorValuesFactory.BuildDoubleBarSensorValue();

            var commonSensorValue = new CommonSensorValue()
            {
                SensorType = SensorType.DoubleBarSensor,
                TypedValue = JsonSerializer.Serialize(expected),
            };

            var actual = commonSensorValue.Convert<DoubleBarSensorValue>();

            Assert.Equal(expected.Max, actual.Max);
            Assert.Equal(expected.Mean, actual.Mean);
            Assert.Equal(expected.Min, actual.Min);
            Assert.Equal(expected.LastValue, actual.LastValue);
            TestTwoBarSensorValues(expected, actual);
        }

        [Fact]
        public void CommonSensorValueToFileSensorBytesValueTest()
        {
            var expected = _sensorValuesFactory.BuildFileSensorBytesValue();

            var commonSensorValue = new CommonSensorValue()
            {
                SensorType = SensorType.FileSensorBytes,
                TypedValue = JsonSerializer.Serialize(expected),
            };

            var actual = commonSensorValue.Convert<FileSensorBytesValue>();

            Assert.Equal(expected.FileName, actual.FileName);
            Assert.Equal(expected.FileContent, actual.FileContent);
            Assert.Equal(expected.Extension, actual.Extension);
            TestTwoSensorValues(expected, actual);
        }

        [Fact]
        public void CommonSensorValueToFileSensorValueTest()
        {
            var expected = _sensorValuesFactory.BuildFileSensorValue();

            var commonSensorValue = new CommonSensorValue()
            {
                SensorType = SensorType.FileSensor,
                TypedValue = JsonSerializer.Serialize(expected),
            };

            var actual = commonSensorValue.Convert<FileSensorValue>();

            Assert.Equal(expected.FileName, actual.FileName);
            Assert.Equal(expected.FileContent, actual.FileContent);
            Assert.Equal(expected.Extension, actual.Extension);
            TestTwoSensorValues(expected, actual);
        }


        private static void TestTwoBarSensorValues(BarSensorValueBase expected, BarSensorValueBase actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            Assert.Equal(expected.StartTime, actual.StartTime);
            Assert.Equal(expected.EndTime, actual.EndTime);
            TestTwoSensorValues(expected, actual);
        }

        private static void TestTwoSensorValues(SensorValueBase expected, SensorValueBase actual)
        {
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.Comment, actual.Comment);
            Assert.Equal(expected.Time, actual.Time);
            Assert.Equal(expected.Description, actual.Description);
            Assert.Equal(expected.Key, actual.Key);
        }
    }
}
