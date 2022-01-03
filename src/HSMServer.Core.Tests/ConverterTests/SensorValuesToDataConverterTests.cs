using System;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Converters;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorValuesToDataConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly SensorValuesTester _sensorValuesTester;
        private readonly string _productName;
        private readonly DateTime _timeCollected;


        public SensorValuesToDataConverterTests(EntitiesConverterFixture converterFixture)
        {
            _sensorValuesFactory = converterFixture.SensorValuesFactory;
            _sensorValuesTester = converterFixture.SensorValuesTester;
            _productName = EntitiesConverterFixture.ProductKey;

            _timeCollected = DateTime.UtcNow;
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor, TransactionType.Add)]
        [InlineData(SensorType.IntSensor, TransactionType.Delete)]
        [InlineData(SensorType.DoubleSensor, TransactionType.Unknown)]
        [InlineData(SensorType.StringSensor, TransactionType.Update)]
        [InlineData(SensorType.IntegerBarSensor, TransactionType.UpdateTree)]
        [InlineData(SensorType.DoubleBarSensor, TransactionType.Add)]
        [InlineData(SensorType.FileSensorBytes, TransactionType.Update)]
        [InlineData(SensorType.FileSensor, TransactionType.UpdateTree)]
        [Trait("Category", "Simple")]
        public void SensorValueToSensorDataEntityConverterTest(SensorType sensorType, TransactionType transactiontype)
        {
            var boolSensorValue = _sensorValuesFactory.BuildSensorValue(sensorType);

            var data = boolSensorValue.Convert(_productName, _timeCollected, transactiontype);

            _sensorValuesTester.TestSensorData(boolSensorValue, data, _timeCollected, transactiontype);
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor, null)]
        [InlineData(SensorType.IntSensor, "")]
        [InlineData(SensorType.DoubleSensor, null)]
        [InlineData(SensorType.StringSensor, "")]
        [InlineData(SensorType.IntegerBarSensor, null)]
        [InlineData(SensorType.DoubleBarSensor, "")]
        [InlineData(SensorType.FileSensorBytes, null)]
        [InlineData(SensorType.FileSensor, "")]
        [Trait("Category", "Without comment")]
        public void SensorValueToSensorDataEntityConverter_WithoutComment_Test(SensorType sensorType, string comment)
        {
            var sensorValue = _sensorValuesFactory.BuildSensorValue(sensorType);
            sensorValue.Comment = comment;

            var data = sensorValue.Convert(_productName, _timeCollected, TransactionType.Unknown);

            Assert.Equal(GetSensorDataStringValue(sensorValue), data.StringValue);
        }


        private string GetSensorDataStringValue(SensorValueBase sensorValue) =>
            sensorValue switch
            {
                BoolSensorValue boolSensorValue => SensorDataStringValuesFactory.GetSimpleSensorsString(_timeCollected, boolSensorValue.Comment, boolSensorValue.BoolValue),
                IntSensorValue intSensorValue => SensorDataStringValuesFactory.GetSimpleSensorsString(_timeCollected, intSensorValue.Comment, intSensorValue.IntValue),
                DoubleSensorValue doubleSensorValue => SensorDataStringValuesFactory.GetSimpleSensorsString(_timeCollected, doubleSensorValue.Comment, doubleSensorValue.DoubleValue),
                StringSensorValue stringSensorValue => SensorDataStringValuesFactory.GetSimpleSensorsString(_timeCollected, stringSensorValue.Comment, stringSensorValue.StringValue),
                IntBarSensorValue intBarSensorValue => SensorDataStringValuesFactory.GetBarSensorsString(_timeCollected, intBarSensorValue.Comment, intBarSensorValue.Min, intBarSensorValue.Mean, intBarSensorValue.Max, intBarSensorValue.Count, intBarSensorValue.LastValue),
                DoubleBarSensorValue doubleBarSensorValue => SensorDataStringValuesFactory.GetBarSensorsString(_timeCollected, doubleBarSensorValue.Comment, doubleBarSensorValue.Min, doubleBarSensorValue.Mean, doubleBarSensorValue.Max, doubleBarSensorValue.Count, doubleBarSensorValue.LastValue),
                FileSensorBytesValue fileSensorBytesValue => SensorDataStringValuesFactory.GetFileSensorsString(_timeCollected, fileSensorBytesValue.Comment, fileSensorBytesValue.FileName, fileSensorBytesValue.Extension, fileSensorBytesValue.FileContent.Length),
                FileSensorValue fileSensorValue => SensorDataStringValuesFactory.GetFileSensorsString(_timeCollected, fileSensorValue.Comment, fileSensorValue.FileName, fileSensorValue.Extension, fileSensorValue.FileContent.Length),
                _ => null,
            };
    }
}
