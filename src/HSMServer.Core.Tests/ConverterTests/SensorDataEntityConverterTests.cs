using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Converters;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.Tests.Infrastructure;
using System.Text.Json;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorDataEntityConverterTests
    {
        private static readonly string _productName = EntitiesConverterFixture.ProductName;


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "to SensorData")]
        public void SensorDataEntityToSensorDataConverterTest(SensorType type)
        {
            (SensorDataEntity sensorDataEntity, SensorInfo sensorInfo) = BuildDatasForTests(type);

            var data = sensorDataEntity.Convert(sensorInfo, sensorInfo.ProductName);

            TestConvertSensorDataEntityToSensorData(sensorDataEntity, sensorInfo, data);
        }

        [Fact]
        [Trait("Category", "to SensorData")]
        public void SensorDataEntityToSensorDataConverter_FileSensorToFileSensorBytes_Test()
        {
            (SensorDataEntity fileSensorDataEntity, SensorInfo sensorInfo) = BuildDatasForTests(SensorType.FileSensor);
            var expectedSensorDataEntity = GetExpectedFileSensorBytesDataEntity(fileSensorDataEntity);

            var data = fileSensorDataEntity.Convert(sensorInfo, sensorInfo.ProductName);

            TestConvertSensorDataEntityToSensorData(expectedSensorDataEntity, sensorInfo, data);
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "to SensorData without comment")]
        public void SensorDataEntityToSensorDataConverter_WithoutComment_Test(SensorType type)
        {
            (SensorDataEntity sensorDataEntity, SensorInfo sensorInfo) = BuildDatasForTests(type, false);

            var data = sensorDataEntity.Convert(sensorInfo, sensorInfo.ProductName);

            TestSensorDataStringValues(sensorDataEntity, data, (SensorType)sensorDataEntity.DataType);
        }

        [Fact]
        [Trait("Category", "to SensorData without comment")]
        public void SensorDataEntityToSensorDataConverter_FileSensorToFileSensorBytes_WithoutComment_Test()
        {
            (SensorDataEntity fileSensorDataEntity, SensorInfo sensorInfo) = BuildDatasForTests(SensorType.FileSensor, false);
            var expectedSensorDataEntity = GetExpectedFileSensorBytesDataEntity(fileSensorDataEntity);

            var data = fileSensorDataEntity.Convert(sensorInfo, sensorInfo.ProductName);

            TestSensorDataStringValues(expectedSensorDataEntity, data, (SensorType)expectedSensorDataEntity.DataType);
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "to SensorData without SensorInfo")]
        public void SensorDataEntityToSensorDataConverter_WithoutSensorInfo_Test(SensorType type)
        {
            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(type);

            var data = sensorDataEntity.Convert(_productName);

            TestSensorDataCommonProperties(sensorDataEntity, data, _productName, (SensorType)sensorDataEntity.DataType);
        }

        [Fact]
        [Trait("Category", "to SensorData without SensorInfo")]
        public void SensorDataEntityToSensorDataConverter_FileSensorToFileSensorBytes_WithoutSensorInfo_Test()
        {
            var fileSensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(SensorType.FileSensor);
            var expectedSensorDataEntity = GetExpectedFileSensorBytesDataEntity(fileSensorDataEntity);

            var data = fileSensorDataEntity.Convert(_productName);

            TestSensorDataCommonProperties(expectedSensorDataEntity, data, _productName, (SensorType)expectedSensorDataEntity.DataType);
        }


        private static void TestConvertSensorDataEntityToSensorData(SensorDataEntity sensorDataEntity, SensorInfo sensorInfo, SensorData data)
        {
            Assert.Equal(sensorInfo.Description, data.Description);

            TestSensorDataCommonProperties(sensorDataEntity, data, sensorInfo.ProductName, (SensorType)sensorDataEntity.DataType);
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
            }
        }


        private static (SensorDataEntity, SensorInfo) BuildDatasForTests(SensorType sensorType, bool withComment = true)
        {
            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(sensorType, withComment);
            var sensorInfo = SensorInfoFactory.BuildSensorInfo(_productName, sensorDataEntity.DataType);

            return (sensorDataEntity, sensorInfo);
        }

        private static SensorDataEntity GetExpectedFileSensorBytesDataEntity(SensorDataEntity dataEntity) =>
            new()
            {
                Path = dataEntity.Path,
                Status = dataEntity.Status,
                Time = dataEntity.Time,
                Timestamp = dataEntity.Timestamp,
                TimeCollected = dataEntity.TimeCollected,
                DataType = (byte)SensorType.FileSensorBytes,
                TypedData = GetTypedDataForFileSensorBytes(dataEntity.TypedData),
            };

        private static string GetTypedDataForFileSensorBytes(string fileSensorTypedData)
        {
            var fileSensorData = JsonSerializer.Deserialize<FileSensorData>(fileSensorTypedData);
            var fileSensorBytesData = new FileSensorBytesData()
            {
                Comment = fileSensorData.Comment,
                Extension = fileSensorData.Extension,
                FileContent = System.Text.Encoding.UTF8.GetBytes(fileSensorData.FileContent),
                FileName = fileSensorData.FileName,
            };

            return JsonSerializer.Serialize(fileSensorBytesData);
        }
    }
}
