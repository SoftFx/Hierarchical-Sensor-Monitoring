using System;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Tests.MonitoringDataReceiverTests;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorValuesToDataEntityConverterTests : IClassFixture<SensorValuesToDataEntityConverterFixture>
    {
        private readonly Converter _converter;
        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly DateTime _timeCollected;


        public SensorValuesToDataEntityConverterTests(SensorValuesToDataEntityConverterFixture converterFixture)
        {
            _sensorValuesFactory = converterFixture.SensorValuesFactory;

            var converterLogger = CommonMoqs.CreateNullLogger<Converter>();
            _converter = new Converter(converterLogger);

            _timeCollected = DateTime.UtcNow;
        }


        [Fact]
        public void BoolSensorValueToSensorDataEntityConverterTest()
        {
            var boolSensorValue = _sensorValuesFactory.BuildBoolSensorValue();

            var dataEntity = _converter.ConvertToDatabase(boolSensorValue, _timeCollected, HSMSensorDataObjects.SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(boolSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        public void IntSensorValueToSensorDataEntityConverterTest()
        {
            var intSensorValue = _sensorValuesFactory.BuildIntSensorValue();

            var dataEntity = _converter.ConvertToDatabase(intSensorValue, _timeCollected, HSMSensorDataObjects.SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(intSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        public void DoubleSensorValueToSensorDataEntityConverterTest()
        {
            var doubleSensorValue = _sensorValuesFactory.BuildDoubleSensorValue();

            var dataEntity = _converter.ConvertToDatabase(doubleSensorValue, _timeCollected, HSMSensorDataObjects.SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(doubleSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        public void StringSensorValueToSensorDataEntityConverterTest()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();

            var dataEntity = _converter.ConvertToDatabase(stringSensorValue, _timeCollected, HSMSensorDataObjects.SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(stringSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        public void IntBarSensorValueToSensorDataEntityConverterTest()
        {
            var intBarSensorValue = _sensorValuesFactory.BuildIntBarSensorValue();

            var dataEntity = _converter.ConvertToDatabase(intBarSensorValue, _timeCollected, HSMSensorDataObjects.SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(intBarSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        public void DoubleBarSensorValueToSensorDataEntityConverterTest()
        {
            var doubleBarSensorValue = _sensorValuesFactory.BuildDoubleBarSensorValue();

            var dataEntity = _converter.ConvertToDatabase(doubleBarSensorValue, _timeCollected, HSMSensorDataObjects.SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(doubleBarSensorValue, dataEntity, _timeCollected);
        }

        [Fact]
        public void FileSensorBytesValueToSensorDataEntityConverterTest()
        {
            var fileSensorBytesValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            var dataEntity = _converter.ConvertToDatabase(fileSensorBytesValue, _timeCollected, HSMSensorDataObjects.SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(fileSensorBytesValue, dataEntity, _timeCollected);
        }

        [Fact]
        public void FileSensorValueToSensorDataEntityConverterTest()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();

            var dataEntity = _converter.ConvertToDatabase(fileSensorValue, _timeCollected, HSMSensorDataObjects.SensorStatus.Ok);

            SensorValuesTester.TestSensorDataEntity(fileSensorValue, dataEntity, _timeCollected);
        }
    }
}
