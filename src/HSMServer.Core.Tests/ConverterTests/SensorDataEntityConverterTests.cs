using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Converters;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorDataEntityConverterTests
    {
        private static readonly string _productName = EntitiesConverterFixture.ProductKey;


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [InlineData(SensorType.FileSensor)]
        [Trait("Category", "to SensorData")]
        public void SensorDataEntityToSensorDataConverterTest(SensorType type)
        {
            (SensorDataEntity sensorDataEntity, SensorInfo sensorInfo) = BuildDatasForTests(type);

            var data = sensorDataEntity.Convert(sensorInfo, sensorInfo.ProductName);

            TestConvertSensorDataEntityToSensorData(sensorDataEntity, sensorInfo, data);
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [InlineData(SensorType.FileSensor)]
        [Trait("Category", "to SensorData without comment")]
        public void SensorDataEntityToSensorDataConverter_WithoutComment_Test(SensorType type)
        {
            (SensorDataEntity sensorDataEntity, SensorInfo sensorInfo) = BuildDatasForTests(type, false);

            var data = sensorDataEntity.Convert(sensorInfo, sensorInfo.ProductName);

            TestSensorDataStringValues(sensorDataEntity, data, (SensorType)sensorDataEntity.DataType);
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [InlineData(SensorType.FileSensor)]
        [Trait("Category", "to SensorData without SensorInfo")]
        public void SensorDataEntityToSensorDataConverter_WithoutSensorInfo_Test(SensorType type)
        {
            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(type);

            var data = sensorDataEntity.Convert(_productName);

            TestSensorDataCommonProperties(sensorDataEntity, data, _productName, (SensorType)sensorDataEntity.DataType);
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

                case SensorType.FileSensor:
                    FileSensorData fileSensorData = JsonSerializer.Deserialize<FileSensorData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsString(expected.TimeCollected, fileSensorData.Comment, fileSensorData.FileName, fileSensorData.Extension, fileSensorData.FileContent.Length),
                                 actual.StringValue);
                    Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsShortString(fileSensorData.FileName, fileSensorData.Extension, fileSensorData.FileContent.Length),
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
    }
}
