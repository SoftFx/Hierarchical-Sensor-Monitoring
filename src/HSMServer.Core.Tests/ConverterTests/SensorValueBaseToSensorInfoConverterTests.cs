using System.Collections.Generic;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorValueBaseToSensorInfoConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private const char SensorPathSeparator = '/';

        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly SensorValuesTester _sensorValuesTester;
        private readonly string _productName = EntitiesConverterFixture.ProductKey;
        private readonly Converter _converter;


        public SensorValueBaseToSensorInfoConverterTests(EntitiesConverterFixture fixture)
        {
            _converter = new Converter(CommonMoqs.CreateNullLogger<Converter>());
            _sensorValuesFactory = fixture.SensorValuesFactory;
            _sensorValuesTester = fixture.SensorValuesTester;
        }


        [Fact]
        [Trait("Category", "Simple")]
        public void BoolSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildBoolSensorValue();

            var sensorInfo = _converter.Convert(_productName, sensorValue);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void IntSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildIntSensorValue();

            var sensorInfo = _converter.Convert(_productName, sensorValue);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void DoubleSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildDoubleSensorValue();

            var sensorInfo = _converter.Convert(_productName, sensorValue);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void StringSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildStringSensorValue();

            var sensorInfo = _converter.Convert(_productName, sensorValue);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void IntBarSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildIntBarSensorValue();

            var sensorInfo = _converter.Convert(_productName, sensorValue);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void DoubleBarSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildDoubleBarSensorValue();

            var sensorInfo = _converter.Convert(_productName, sensorValue);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void FileSensorBytesValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            var sensorInfo = _converter.Convert(_productName, sensorValue);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void FileSensorValueToSensorInfoConverterTest()
        {
            var sensorValue = _sensorValuesFactory.BuildFileSensorValue();

            var sensorInfo = _converter.Convert(_productName, sensorValue);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "With complex path")]
        public void FileSensorValueToSensorInfoConverter_WithComplexPath_Test()
        {
            var sensorValue = _sensorValuesFactory.BuildFileSensorValue();
            sensorValue.Path = GetSensorPath(_productName, sensorValue.Path);

            var sensorInfo = _converter.Convert(_productName, sensorValue);

            Assert.Equal(GetSensorName(sensorValue.Path), sensorInfo.SensorName);
        }


        private static string GetSensorPath(string productName, string sensorName) =>
            string.Join(SensorPathSeparator, new List<string> { productName, sensorName });

        private static string GetSensorName(string path) => path.Split(SensorPathSeparator)?[^1];
    }
}
