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
        private readonly string _productName = $"{nameof(SensorInfo)} product name";

        private delegate SensorData ConvertDataEntityToData(SensorDataEntity dataObject, SensorInfo sensorInfo, string productName);
        private delegate SensorData SimpleConvertDataEntityToData(SensorDataEntity dataObject, string productName);
        private delegate SensorHistoryData ConvertDataEntityToHistoryData(SensorDataEntity dataObject);


        public SensorDataEntityConverterTests()
        {
            var converterLogger = CommonMoqs.CreateNullLogger<Converter>();
            _converter = new Converter(converterLogger);
        }


        [Fact]
        [Trait("Category", "to SensorData")]
        public void BoolSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.BooleanSensor);

        [Fact]
        [Trait("Category", "to SensorData")]
        public void IntSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.IntSensor);

        [Fact]
        [Trait("Category", "to SensorData")]
        public void DoubleSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.DoubleSensor);

        [Fact]
        [Trait("Category", "to SensorData")]
        public void StringSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.StringSensor);

        [Fact]
        [Trait("Category", "to SensorData")]
        public void IntBarSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.IntegerBarSensor);

        [Fact]
        [Trait("Category", "to SensorData")]
        public void DoubleBarSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.DoubleBarSensor);

        [Fact]
        [Trait("Category", "to SensorData")]
        public void FileBytesSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.FileSensorBytes);

        [Fact]
        [Trait("Category", "to SensorData")]
        public void FileSensorDataEntityToSensorDataConverterTest() =>
            TestConvertSensorDataEntityToSensorData(_converter.Convert, SensorType.FileSensor);


        [Fact]
        [Trait("Category", "to SensorData without comment")]
        public void BoolSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.BooleanSensor);

        [Fact]
        [Trait("Category", "to SensorData without comment")]
        public void IntSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.IntSensor);

        [Fact]
        [Trait("Category", "to SensorData without comment")]
        public void DoubleSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.DoubleSensor);

        [Fact]
        [Trait("Category", "to SensorData without comment")]
        public void StringSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.StringSensor);

        [Fact]
        [Trait("Category", "to SensorData without comment")]
        public void IntBarSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.IntegerBarSensor);

        [Fact]
        [Trait("Category", "to SensorData without comment")]
        public void DoubleBarSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.DoubleBarSensor);

        [Fact]
        [Trait("Category", "to SensorData without comment")]
        public void FileBytesSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.FileSensorBytes);

        [Fact]
        [Trait("Category", "to SensorData without comment")]
        public void FileSensorDataEntityToSensorDataConverter_WithoutComment_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutComment(_converter.Convert, SensorType.FileSensor);


        [Fact]
        [Trait("Category", "to SensorData without SensorInfo")]
        public void FileBytesSensorDataEntityToSensorDataConverter_WithoutSensorInfo_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutSensorInfo(_converter.Convert, SensorType.FileSensorBytes, _productName);

        [Fact]
        [Trait("Category", "to SensorData without SensorInfo")]
        public void FileSensorDataEntityToSensorDataConverter_WithoutSensorInfo_Test() =>
            TestConvertSensorDataEntityToSensorData_WithoutSensorInfo(_converter.Convert, SensorType.FileSensor, _productName);


        [Fact]
        [Trait("Category", "to SensorHistoryData")]
        public void BoolSensorDataEntityToSensorHistoryDataConverterTest() =>
            TestSensorDataEntityToSensorHistoryDataConverter(_converter.Convert, SensorType.BooleanSensor);

        [Fact]
        [Trait("Category", "to SensorHistoryData")]
        public void IntSensorDataEntityToSensorHistoryDataConverterTest() =>
            TestSensorDataEntityToSensorHistoryDataConverter(_converter.Convert, SensorType.IntSensor);

        [Fact]
        [Trait("Category", "to SensorHistoryData")]
        public void DoubleBarSensorDataEntityToSensorHistoryDataConverterTest() =>
            TestSensorDataEntityToSensorHistoryDataConverter(_converter.Convert, SensorType.DoubleBarSensor);

        [Fact]
        [Trait("Category", "to SensorHistoryData")]
        public void FileBytesSensorDataEntityToSensorHistoryDataConverterTest() =>
            TestSensorDataEntityToSensorHistoryDataConverter(_converter.Convert, SensorType.FileSensorBytes);


        private static void TestConvertSensorDataEntityToSensorData(ConvertDataEntityToData convert, SensorType sensorType)
        {
            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(sensorType);
            var sensorInfo = BuildSensorInfo(sensorDataEntity.DataType);

            var data = convert(sensorDataEntity, sensorInfo, sensorInfo.ProductName);

            Assert.Equal(sensorInfo.Description, data.Description);

            TestSensorDataCommonProperties(sensorDataEntity, data, sensorInfo.ProductName, sensorType);
        }

        private static void TestConvertSensorDataEntityToSensorData_WithoutComment(ConvertDataEntityToData convert, SensorType sensorType)
        {
            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(sensorType, false);
            var sensorInfo = BuildSensorInfo(sensorDataEntity.DataType);

            var data = convert(sensorDataEntity, sensorInfo, sensorInfo.ProductName);

            TestSensorDataStringValues(sensorDataEntity, data, sensorType);
        }

        private static void TestConvertSensorDataEntityToSensorData_WithoutSensorInfo(SimpleConvertDataEntityToData convert,
            SensorType sensorType, string productName)
        {
            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(sensorType);

            var data = convert(sensorDataEntity, productName);

            TestSensorDataCommonProperties(sensorDataEntity, data, productName, sensorType);
        }

        private static void TestSensorDataCommonProperties(SensorDataEntity expected, SensorData actual, string productName, SensorType sensorType)
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(productName, actual.Product);
            Assert.Equal(expected.TimeCollected, actual.Time);
            Assert.Equal((SensorType)expected.DataType, actual.SensorType);
            Assert.Equal((SensorStatus)expected.Status, actual.Status);
            Assert.Equal(TransactionType.Unknown, actual.TransactionType);
            Assert.True(string.IsNullOrEmpty(actual.Key));
            Assert.True(string.IsNullOrEmpty(actual.ValidationError));

            TestSensorDataStringValues(expected, actual, sensorType);
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

        private static void TestSensorDataEntityToSensorHistoryDataConverter(ConvertDataEntityToHistoryData convert, SensorType sensorType)
        {
            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(sensorType);

            var historyData = convert(sensorDataEntity);

            Assert.NotNull(historyData);
            Assert.Equal(sensorDataEntity.TypedData, historyData.TypedData);
            Assert.Equal((SensorType)sensorDataEntity.DataType, historyData.SensorType);
            Assert.Equal(sensorDataEntity.Time, historyData.Time);
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
