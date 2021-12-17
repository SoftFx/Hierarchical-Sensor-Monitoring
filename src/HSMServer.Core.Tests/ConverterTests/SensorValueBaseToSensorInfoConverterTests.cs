using System.Collections.Generic;
using HSMServer.Core.Converters;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorValueBaseToSensorInfoConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private const char SensorPathSeparator = '/';

        private readonly string _productName = EntitiesConverterFixture.ProductKey;

        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly SensorValuesTester _sensorValuesTester;


        public SensorValueBaseToSensorInfoConverterTests(EntitiesConverterFixture fixture)
        {
            _sensorValuesFactory = fixture.SensorValuesFactory;
            _sensorValuesTester = fixture.SensorValuesTester;
        }


        [Fact]
        [Trait("Category", "Simple")]
        public void BoolSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildBoolSensorValue();

            var sensorInfo = sensorValue.Convert(_productName);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void IntSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildIntSensorValue();

            var sensorInfo = sensorValue.Convert(_productName);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void DoubleSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildDoubleSensorValue();

            var sensorInfo = sensorValue.Convert(_productName);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void StringSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildStringSensorValue();

            var sensorInfo = sensorValue.Convert(_productName);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void IntBarSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildIntBarSensorValue();

            var sensorInfo = sensorValue.Convert(_productName);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void DoubleBarSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildDoubleBarSensorValue();

            var sensorInfo = sensorValue.Convert(_productName);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void FileSensorBytesValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            var sensorInfo = sensorValue.Convert(_productName);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void FileSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildFileSensorValue();

            var sensorInfo = sensorValue.Convert(_productName);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "With complex path")]
        public void FileSensorValueToSensorInfoConverter_WithComplexPath_Test()
        {
            var sensorValue = _sensorValuesFactory.BuildFileSensorValue();
            sensorValue.Path = GetSensorPath(_productName, sensorValue.Path);

            var sensorInfo = sensorValue.Convert(_productName);

            Assert.Equal(GetSensorName(sensorValue.Path), sensorInfo.SensorName);
        }


        private static string GetSensorPath(string productName, string sensorName) =>
            string.Join(SensorPathSeparator, new List<string> { productName, sensorName });

        private static string GetSensorName(string path) => path.Split(SensorPathSeparator)?[^1];
    }
}
