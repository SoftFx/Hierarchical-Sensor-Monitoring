using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Model.HistoryValues;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class BaseValueToHistoryConverterTests
    {
        [Theory]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.Rate)]
        [InlineData(SensorType.Enum)]
        [InlineData(SensorType.TimeSpan)]
        [InlineData(SensorType.Version)]
        [Trait("Category", "Simple")]
        public void Convert_BaseValue_ReturnsNonNullResult(SensorType type)
        {
            var value = SensorValuesFactory.BuildValue(type);

            var result = value.Convert();

            Assert.NotNull(result);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void Convert_TimeSpanValue_ReturnsSimpleSensorHistoryWithValueString()
        {
            var expected = TimeSpan.FromMinutes(5);
            var value = new TimeSpanValue
            {
                Comment = "timespan comment",
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = expected,
            };

            var result = value.Convert() as SimpleSensorHistory;

            Assert.NotNull(result);
            Assert.Equal(expected.ToString(), result.Value);
            Assert.Equal("timespan comment", result.Comment);
            Assert.Equal(value.Time, result.Time);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void Convert_VersionValue_ReturnsSimpleSensorHistoryWithValueString()
        {
            var expected = new Version(1, 2, 3);
            var value = new VersionValue
            {
                Comment = "version comment",
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = expected,
            };

            var result = value.Convert() as SimpleSensorHistory;

            Assert.NotNull(result);
            Assert.Equal(expected.ToString(), result.Value);
            Assert.Equal("version comment", result.Comment);
            Assert.Equal(value.Time, result.Time);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void Convert_List_PreservesTimeSpanAndVersionValues()
        {
            var values = new List<BaseValue>
            {
                SensorValuesFactory.BuildValue(SensorType.TimeSpan),
                SensorValuesFactory.BuildValue(SensorType.Version),
                SensorValuesFactory.BuildValue(SensorType.Boolean),
            };

            var result = values.Convert();

            Assert.Equal(values.Count, result.Count);
            Assert.All(result, Assert.NotNull);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void Convert_List_DropsNullEntries()
        {
            var values = new List<BaseValue>
            {
                SensorValuesFactory.BuildValue(SensorType.TimeSpan),
                null,
            };

            var result = values.Convert();

            Assert.Single(result);
        }
    }
}
