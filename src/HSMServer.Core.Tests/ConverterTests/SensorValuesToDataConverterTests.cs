using System;
using HSMServer.Core.Converters;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorValuesToDataConverterTests : IClassFixture<SensorValuesToDataEntityConverterFixture>
    {
        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly SensorValuesTester _sensorValuesTester;
        private readonly string _productName;
        private readonly DateTime _timeCollected;


        public SensorValuesToDataConverterTests(SensorValuesToDataEntityConverterFixture converterFixture)
        {
            _sensorValuesFactory = converterFixture.SensorValuesFactory;
            _sensorValuesTester = converterFixture.SensorValuesTester;
            _productName = SensorValuesToDataEntityConverterFixture.ProductKey;

            _timeCollected = DateTime.UtcNow;
        }


        [Fact]
        [Trait("Category", "Simple")]
        public void BoolSensorValueToSensorDataEntityConverterTest()
        {
            const TransactionType type = TransactionType.Add;

            var boolSensorValue = _sensorValuesFactory.BuildBoolSensorValue();

            var data = boolSensorValue.Convert(_productName, _timeCollected, type);

            _sensorValuesTester.TestSensorData(boolSensorValue, data, _timeCollected, type);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void IntSensorValueToSensorDataEntityConverterTest()
        {
            const TransactionType type = TransactionType.Delete;

            var intSensorValue = _sensorValuesFactory.BuildIntSensorValue();

            var data = intSensorValue.Convert(_productName, _timeCollected, type);

            _sensorValuesTester.TestSensorData(intSensorValue, data, _timeCollected, type);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void DoubleSensorValueToSensorDataEntityConverterTest()
        {
            const TransactionType type = TransactionType.Unknown;

            var doubleSensorValue = _sensorValuesFactory.BuildDoubleSensorValue();

            var data = doubleSensorValue.Convert(_productName, _timeCollected, type);

            _sensorValuesTester.TestSensorData(doubleSensorValue, data, _timeCollected, type);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void StringSensorValueToSensorDataEntityConverterTest()
        {
            const TransactionType type = TransactionType.Update;

            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();

            var data = stringSensorValue.Convert(_productName, _timeCollected, type);

            _sensorValuesTester.TestSensorData(stringSensorValue, data, _timeCollected, type);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void IntBarSensorValueToSensorDataEntityConverterTest()
        {
            const TransactionType type = TransactionType.UpdateTree;

            var intBarSensorValue = _sensorValuesFactory.BuildIntBarSensorValue();

            var data = intBarSensorValue.Convert(_productName, _timeCollected, type);

            _sensorValuesTester.TestSensorData(intBarSensorValue, data, _timeCollected, type);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void DoubleBarSensorValueToSensorDataEntityConverterTest()
        {
            const TransactionType type = TransactionType.Add;

            var doubleBarSensorValue = _sensorValuesFactory.BuildDoubleBarSensorValue();

            var data = doubleBarSensorValue.Convert(_productName, _timeCollected, type);

            _sensorValuesTester.TestSensorData(doubleBarSensorValue, data, _timeCollected, type);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void FileSensorBytesValueToSensorDataEntityConverterTest()
        {
            const TransactionType type = TransactionType.Update;

            var fileSensorBytesValue = _sensorValuesFactory.BuildFileSensorBytesValue();

            var data = fileSensorBytesValue.Convert(_productName, _timeCollected, type);

            _sensorValuesTester.TestSensorData(fileSensorBytesValue, data, _timeCollected, type);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void FileSensorValueToSensorDataEntityConverterTest()
        {
            const TransactionType type = TransactionType.UpdateTree;

            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();

            var data = fileSensorValue.Convert(_productName, _timeCollected, type);

            _sensorValuesTester.TestSensorData(fileSensorValue, data, _timeCollected, type);
        }


        [Fact]
        [Trait("Category", "Without comment")]
        public void BoolSensorValueToSensorDataEntityConverter_WithoutComment_Test()
        {
            var boolSensorValue = _sensorValuesFactory.BuildBoolSensorValue();
            boolSensorValue.Comment = null;

            var data = boolSensorValue.Convert(_productName, _timeCollected, TransactionType.Unknown);

            Assert.Equal($"Time: {_timeCollected.ToUniversalTime():G}. Value = {boolSensorValue.BoolValue}.", data.StringValue);
        }

        [Fact]
        [Trait("Category", "Without comment")]
        public void IntSensorValueToSensorDataEntityConverter_WithoutComment_Test()
        {
            var intSensorValue = _sensorValuesFactory.BuildIntSensorValue();
            intSensorValue.Comment = string.Empty;

            var data = intSensorValue.Convert(_productName, _timeCollected, TransactionType.Unknown);

            Assert.Equal($"Time: {_timeCollected.ToUniversalTime():G}. Value = {intSensorValue.IntValue}.", data.StringValue);
        }

        [Fact]
        [Trait("Category", "Without comment")]
        public void DoubleSensorValueToSensorDataEntityConverter_WithoutComment_Test()
        {
            var doubleSensorValue = _sensorValuesFactory.BuildDoubleSensorValue();
            doubleSensorValue.Comment = null;

            var data = doubleSensorValue.Convert(_productName, _timeCollected, TransactionType.Unknown);

            Assert.Equal($"Time: {_timeCollected.ToUniversalTime():G}. Value = {doubleSensorValue.DoubleValue}.", data.StringValue);
        }

        [Fact]
        [Trait("Category", "Without comment")]
        public void StringSensorValueToSensorDataEntityConverter_WithoutComment_Test()
        {
            var stringSensorValue = _sensorValuesFactory.BuildStringSensorValue();
            stringSensorValue.Comment = string.Empty;

            var data = stringSensorValue.Convert(_productName, _timeCollected, TransactionType.Unknown);

            Assert.Equal($"Time: {_timeCollected.ToUniversalTime():G}. Value = {stringSensorValue.StringValue}.", data.StringValue);
        }

        [Fact]
        [Trait("Category", "Without comment")]
        public void IntBarSensorValueToSensorDataEntityConverter_WithoutComment_Test()
        {
            var intBarSensorValue = _sensorValuesFactory.BuildIntBarSensorValue();
            intBarSensorValue.Comment = null;

            var data = intBarSensorValue.Convert(_productName, _timeCollected, TransactionType.Unknown);

            Assert.Equal(
                $"Time: {_timeCollected.ToUniversalTime():G}. Value: Min = {intBarSensorValue.Min}," +
                $" Mean = {intBarSensorValue.Mean}, Max = {intBarSensorValue.Max}, Count = {intBarSensorValue.Count}," +
                $" Last = {intBarSensorValue.LastValue}.", data.StringValue);
        }

        [Fact]
        [Trait("Category", "Without comment")]
        public void DoubleBarSensorValueToSensorDataEntityConverter_WithoutComment_Test()
        {
            var doubleBarSensorValue = _sensorValuesFactory.BuildDoubleBarSensorValue();
            doubleBarSensorValue.Comment = string.Empty;

            var data = doubleBarSensorValue.Convert(_productName, _timeCollected, TransactionType.Unknown);

            Assert.Equal(
                $"Time: {_timeCollected.ToUniversalTime():G}. Value: Min = {doubleBarSensorValue.Min}," +
                $" Mean = {doubleBarSensorValue.Mean}, Max = {doubleBarSensorValue.Max}, Count = {doubleBarSensorValue.Count}," +
                $" Last = {doubleBarSensorValue.LastValue}.", data.StringValue);
        }

        [Fact]
        [Trait("Category", "Without comment")]
        public void FileSensorBytesValueToSensorDataEntityConverter_WithoutComment_Test()
        {
            var fileSensorBytesValue = _sensorValuesFactory.BuildFileSensorBytesValue();
            fileSensorBytesValue.Comment = null;

            var data = fileSensorBytesValue.Convert(_productName, _timeCollected, TransactionType.Unknown);

            Assert.Equal(
                $"Time: {_timeCollected.ToUniversalTime():G}. File size: {fileSensorBytesValue.FileContent.Length} bytes." +
                $" File name: {fileSensorBytesValue.FileName}.{fileSensorBytesValue.Extension}.", data.StringValue);
        }

        [Fact]
        [Trait("Category", "Without comment")]
        public void FileSensorValueToSensorDataEntityConverter_WithoutComment_Test()
        {
            var fileSensorValue = _sensorValuesFactory.BuildFileSensorValue();
            fileSensorValue.Comment = string.Empty;

            var data = fileSensorValue.Convert(_productName, _timeCollected, TransactionType.Unknown);

            Assert.Equal(
                $"Time: {_timeCollected.ToUniversalTime():G}. File size: {fileSensorValue.FileContent.Length} bytes." +
                $" File name: {fileSensorValue.FileName}.{fileSensorValue.Extension}.", data.StringValue);
        }
    }
}
