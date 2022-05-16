using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Converters;
using HSMServer.Core.Tests.Infrastructure;
using System.Text.Json;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    // TODO add tests for SensorModel string property
    public class SensorStringBuilderTests
    {
        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [Trait("Category", "GetShortStringValue for SensorDataEntity")]
        public void SensorDataEntityToSensorDataConverterTest(SensorType type)
        {
            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(type);

            var shortString = SensorStringPropertyBuilder.GetShortStringValue(type, sensorDataEntity.TypedData, 0);

            TestSensorDataStringValues(sensorDataEntity, shortString, type);
        }

        [Fact]
        [Trait("Category", "GetShortStringValue for SensorDataEntity")]
        public void SensorDataEntityToSensorDataConverter_FileSensorWithOriginalSize_Test()
        {
            const SensorType type = SensorType.FileSensorBytes;

            var sensorDataEntity = SensorDataEntitiesFactory.BuildSensorDataEntity(type);
            sensorDataEntity.OriginalFileSensorContentSize = RandomGenerator.GetRandomInt(positive: true);

            var shortString = SensorStringPropertyBuilder.GetShortStringValue(type, sensorDataEntity.TypedData, sensorDataEntity.OriginalFileSensorContentSize);

            TestSensorDataStringValues(sensorDataEntity, shortString, type);
        }

        private static void TestSensorDataStringValues(SensorDataEntity expected, string actual, SensorType sensorType)
        {
            switch (sensorType)
            {
                case SensorType.BooleanSensor:
                    BoolSensorData boolData = JsonSerializer.Deserialize<BoolSensorData>(expected.TypedData);
                    Assert.Equal(boolData.BoolValue.ToString(), actual);
                    break;

                case SensorType.IntSensor:
                    IntSensorData intData = JsonSerializer.Deserialize<IntSensorData>(expected.TypedData);
                    Assert.Equal(intData.IntValue.ToString(), actual);
                    break;

                case SensorType.DoubleSensor:
                    DoubleSensorData doubleData = JsonSerializer.Deserialize<DoubleSensorData>(expected.TypedData);
                    Assert.Equal(doubleData.DoubleValue.ToString(), actual);
                    break;

                case SensorType.StringSensor:
                    StringSensorData stringData = JsonSerializer.Deserialize<StringSensorData>(expected.TypedData);
                    Assert.Equal(stringData.StringValue, actual);
                    break;

                case SensorType.IntegerBarSensor:
                    IntBarSensorData intBarData = JsonSerializer.Deserialize<IntBarSensorData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsShortString(intBarData.Min, intBarData.Mean, intBarData.Max, intBarData.Count, intBarData.LastValue),
                                 actual);
                    break;

                case SensorType.DoubleBarSensor:
                    DoubleBarSensorData doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(expected.TypedData);
                    Assert.Equal(SensorDataStringValuesFactory.GetBarSensorsShortString(doubleBarData.Min, doubleBarData.Mean, doubleBarData.Max, doubleBarData.Count, doubleBarData.LastValue),
                                 actual);
                    break;

                case SensorType.FileSensorBytes:
                    TestFileSensorBytesTypedData(expected, actual);
                    break;
            }
        }

        private static void TestFileSensorBytesTypedData(SensorDataEntity expected, string actual)
        {
            FileSensorBytesData data = JsonSerializer.Deserialize<FileSensorBytesData>(expected.TypedData);
            Assert.Equal(SensorDataStringValuesFactory.GetFileSensorsShortString(data.FileName, data.Extension, GetFilesensorBytesDataContentLength(expected, data)), actual);
        }

        private static int GetFilesensorBytesDataContentLength(SensorDataEntity dataEntity, FileSensorBytesData data) =>
            dataEntity.OriginalFileSensorContentSize == 0 ? data.FileContent?.Length ?? 0 : dataEntity.OriginalFileSensorContentSize;
    }
}
