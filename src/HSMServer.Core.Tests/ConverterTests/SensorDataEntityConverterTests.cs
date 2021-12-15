using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorDataEntityConverterTests
    {
        private readonly Converter _converter;

        private delegate SensorData ConvertDataEntityToData(SensorDataEntity dataObject, SensorInfo sensorInfo, string productName);


        public SensorDataEntityConverterTests()
        {
            var converterLogger = CommonMoqs.CreateNullLogger<Converter>();
            _converter = new Converter(converterLogger);
        }


        [Fact]
        [Trait("Category", "Simple")]
        public void BoolSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.BooleanSensor);

        [Fact]
        [Trait("Category", "Simple")]
        public void IntSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.IntSensor);

        [Fact]
        [Trait("Category", "Simple")]
        public void DoubleSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.DoubleSensor);

        [Fact]
        [Trait("Category", "Simple")]
        public void StringSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.StringSensor);

        [Fact]
        [Trait("Category", "Simple")]
        public void IntBarSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.IntegerBarSensor);

        [Fact]
        [Trait("Category", "Simple")]
        public void DoubleBarSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.DoubleBarSensor);

        [Fact]
        [Trait("Category", "Simple")]
        public void FileBytesSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.FileSensorBytes);

        [Fact]
        [Trait("Category", "Simple")]
        public void FileSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.FileSensor);


        [Fact]
        [Trait("Category", "Without comment")]
        public void BoolSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.BooleanSensor);

        [Fact]
        [Trait("Category", "Without comment")]
        public void IntSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.IntSensor);

        [Fact]
        [Trait("Category", "Without comment")]
        public void DoubleSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.DoubleSensor);

        [Fact]
        [Trait("Category", "Without comment")]
        public void StringSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.StringSensor);

        [Fact]
        [Trait("Category", "Without comment")]
        public void IntBarSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.IntegerBarSensor);

        [Fact]
        [Trait("Category", "Without comment")]
        public void DoubleBarSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.DoubleBarSensor);

        [Fact]
        [Trait("Category", "Without comment")]
        public void FileBytesSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.FileSensorBytes);

        [Fact]
        [Trait("Category", "Without comment")]
        public void FileSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.FileSensor);


        private static void TestConvertSensorDataEntityToSensorData(ConvertDataEntityToData convert, SensorType sensorType)
        {
            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(sensorType);
            var sensorInfo = BuildSensorInfo(sensorDataEntity.DataType);

            var data = convert(sensorDataEntity, sensorInfo, sensorInfo.ProductName);

            Assert.NotNull(data);
            Assert.Equal(sensorDataEntity.Path, data.Path);
            Assert.Equal(sensorInfo.ProductName, data.Product);
            Assert.Equal(sensorDataEntity.TimeCollected, data.Time);
            Assert.Equal((SensorType)sensorDataEntity.DataType, data.SensorType);
            Assert.Equal((SensorStatus)sensorDataEntity.Status, data.Status);
            Assert.Equal(TransactionType.Unknown, data.TransactionType);
            Assert.True(string.IsNullOrEmpty(data.Key));
            Assert.True(string.IsNullOrEmpty(data.ValidationError));

            Assert.Equal(sensorInfo.Description, data.Description);

            TestSensorDataStringValues(sensorDataEntity, data, sensorType);
        }

        private static void TestConvertSensorDataEntityToSensorData_WithoutComment(ConvertDataEntityToData convert, SensorType sensorType)
        {
            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(sensorType, false);
            var sensorInfo = BuildSensorInfo(sensorDataEntity.DataType);

            var data = convert(sensorDataEntity, sensorInfo, sensorInfo.ProductName);

            TestSensorDataStringValues(sensorDataEntity, data, sensorType);
        }

        private static void TestSensorDataStringValues(SensorDataEntity expected, SensorData actual, SensorType sensorType)
        {
            switch (sensorType)
            {
                case SensorType.BooleanSensor:
                    BoolSensorData boolData = JsonSerializer.Deserialize<BoolSensorData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.TimeCollected, boolData.Comment, boolData.BoolValue),
                                 actual.StringValue);
                    Assert.Equal(boolData.BoolValue.ToString(), actual.ShortStringValue);
                    break;

                case SensorType.IntSensor:
                    IntSensorData intData = JsonSerializer.Deserialize<IntSensorData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.TimeCollected, intData.Comment, intData.IntValue),
                                 actual.StringValue);
                    Assert.Equal(intData.IntValue.ToString(), actual.ShortStringValue);
                    break;

                case SensorType.DoubleSensor:
                    DoubleSensorData doubleData = JsonSerializer.Deserialize<DoubleSensorData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.TimeCollected, doubleData.Comment, doubleData.DoubleValue),
                                 actual.StringValue);
                    Assert.Equal(doubleData.DoubleValue.ToString(), actual.ShortStringValue);
                    break;

                case SensorType.StringSensor:
                    StringSensorData stringData = JsonSerializer.Deserialize<StringSensorData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetSimpleSensorsString(expected.TimeCollected, stringData.Comment, stringData.StringValue),
                                 actual.StringValue);
                    Assert.Equal(stringData.StringValue, actual.ShortStringValue);
                    break;

                case SensorType.IntegerBarSensor:
                    IntBarSensorData intBarData = JsonSerializer.Deserialize<IntBarSensorData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsString(expected.TimeCollected, intBarData.Comment, intBarData.Min, intBarData.Mean, intBarData.Max, intBarData.Count, intBarData.LastValue),
                                 actual.StringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsShortString(intBarData.Min, intBarData.Mean, intBarData.Max, intBarData.Count, intBarData.LastValue),
                                 actual.ShortStringValue);
                    break;

                case SensorType.DoubleBarSensor:
                    DoubleBarSensorData doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsString(expected.TimeCollected, doubleBarData.Comment, doubleBarData.Min, doubleBarData.Mean, doubleBarData.Max, doubleBarData.Count, doubleBarData.LastValue),
                                 actual.StringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsShortString(doubleBarData.Min, doubleBarData.Mean, doubleBarData.Max, doubleBarData.Count, doubleBarData.LastValue),
                                 actual.ShortStringValue);
                    break;

                case SensorType.FileSensorBytes:
                    FileSensorBytesData fileSensorBytesData = JsonSerializer.Deserialize<FileSensorBytesData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsString(expected.TimeCollected, fileSensorBytesData.Comment, fileSensorBytesData.FileName, fileSensorBytesData.Extension, fileSensorBytesData.FileContent.Length),
                                 actual.StringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsShortString(fileSensorBytesData.FileName, fileSensorBytesData.Extension, fileSensorBytesData.FileContent.Length),
                                 actual.ShortStringValue);
                    break;

                case SensorType.FileSensor:
                    FileSensorData fileSensorData = JsonSerializer.Deserialize<FileSensorData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsString(expected.TimeCollected, fileSensorData.Comment, fileSensorData.FileName, fileSensorData.Extension, fileSensorData.FileContent.Length),
                                 actual.StringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsShortString(fileSensorData.FileName, fileSensorData.Extension, fileSensorData.FileContent.Length),
                                 actual.ShortStringValue);
                    break;
            }
        }


        private static SensorInfo BuildSensorInfo(byte sensorType) =>
            new()
            {
                Path = $"{typeof(SensorInfo)}",
                ProductName = $"{nameof(SensorInfo)} product name",
                SensorName = nameof(SensorInfo),
                Description = $"{nameof(SensorInfo)} {nameof(SensorInfo.Description)}",
                SensorType = (SensorType)sensorType,
                Unit = RandomValuesGenerator.GetRandomString(),
            };
    }
}
