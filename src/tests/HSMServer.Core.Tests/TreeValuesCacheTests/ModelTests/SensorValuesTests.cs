using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.ModelTests
{
    public class SensorValuesTests
    {
        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.File)]
        [Trait("Category", "Sensor value's ShortInfo")]
        public void SensorValue_ShortInfo_Test(SensorType type)
        {
            var value = SensorValuesFactory.BuildSensorValue(type);

            TestSensorValueShortInfo(value);
        }

        [Fact]
        [Trait("Category", "Compressing content")]
        public void FileSensorValue_CompressingContent_Test()
        {
            var fileValue = SensorValuesFactory.BuildFileValue();
            var compressedValue = CompressionHelper.GetCompressedValue(fileValue);

            var actualValue = fileValue.CompressContent();

            Assert.Equal(fileValue.Value.Length, actualValue.OriginalSize);
            Assert.Equal(compressedValue.Value.Length, actualValue.Value.Length);
            Assert.Equal(compressedValue.Value, actualValue.Value);
            Assert.NotEqual(fileValue.Value, actualValue.Value);
        }


        private static void TestSensorValueShortInfo(BaseValue value)
        {
            switch (value.Type)
            {
                case SensorType.Boolean:
                    TestBaseValue<bool>(value);
                    break;
                case SensorType.Integer:
                    TestBaseValue<int>(value);
                    break;
                case SensorType.Double:
                    TestBaseValue<double>(value);
                    break;
                case SensorType.String:
                    TestBaseValue<string>(value);
                    break;
                case SensorType.File:
                    Assert.Equal(GetFileSensorsShortInfo(value as FileValue), value.ShortInfo);
                    break;
                case SensorType.IntegerBar:
                    TestBarBaseValue<int>(value);
                    break;
                case SensorType.DoubleBar:
                    TestBarBaseValue<double>(value);
                    break;
            }
        }

        private static void TestBaseValue<T>(BaseValue value)
        {
            var baseValueT = value as BaseValue<T>;
            Assert.Equal(baseValueT.Value.ToString(), value.ShortInfo);
        }

        private static void TestBarBaseValue<T>(BaseValue value) where T : struct
        {
            var valueT = value as BarBaseValue<T>;
            var expectedShortInfo = $"Min = {valueT.Min}, Mean = {valueT.Mean}, Max = {valueT.Max}, Count = {valueT.Count}, Last = {valueT.LastValue}.";

            Assert.Equal(expectedShortInfo, value.ShortInfo);
        }

        private static string GetFileSensorsShortInfo(FileValue value)
        {
            string sizeString = $"{value.OriginalSize:F2} bytes";
            string fileNameString = $"{value.Name}.{value.Extension}.";

            return $"File size: {sizeString}. File name: {fileNameString}";
        }
    }
}
