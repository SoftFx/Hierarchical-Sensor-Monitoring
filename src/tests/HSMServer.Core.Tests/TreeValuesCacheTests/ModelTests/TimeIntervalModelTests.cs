using System;
using Xunit;
using HSMServer.Core.Model;


namespace HSMServer.Core.Tests.TreeValuesCacheTests.ModelTests
{
    public class TimeIntervalModelTests
    {
        // #1864: the copy a product keeps of its folder's period is judged by
        // its ticks, so a calendar preset, which has none of its own, goes in
        // at its fixed length. With its zero ticks the copy read as "never".
        [Theory]
        [InlineData(TimeInterval.Month, 31)]
        [InlineData(TimeInterval.ThreeMonths, 93)]
        [InlineData(TimeInterval.SixMonths, 186)]
        [InlineData(TimeInterval.Year, 365)]
        public void ToFromFolderModel_CalendarPreset_CarriesThePresetLength(TimeInterval preset, int days)
        {
            var folder = new TimeIntervalModel(preset);

            Assert.True(folder.IsCalendarPreset);
            Assert.Equal(0L, folder.Ticks);

            var copy = folder.ToFromFolderModel();

            Assert.True(copy.IsFromFolder);
            Assert.False(copy.IsCalendarPreset);
            Assert.Equal(TimeSpan.FromDays(days), copy.ToTimeSpan());

            Assert.True(copy.TimeIsUp(DateTime.UtcNow.AddDays(-days - 1)));
            Assert.False(copy.TimeIsUp(DateTime.UtcNow.AddDays(-days + 1)));
        }

        [Fact]
        public void ToFromFolderModel_Ticks_CarriesTheTicks()
        {
            var week = TimeSpan.FromDays(7);

            var copy = new TimeIntervalModel(week.Ticks).ToFromFolderModel();

            Assert.True(copy.IsFromFolder);
            Assert.Equal(week, copy.ToTimeSpan());
        }

        [Fact]
        public void ToFromFolderModel_None_IsNeverUp()
        {
            var copy = TimeIntervalModel.None.ToFromFolderModel();

            Assert.True(copy.IsFromFolder);
            Assert.Equal(long.MaxValue, copy.Ticks);
            Assert.False(copy.TimeIsUp(DateTime.MinValue));
        }

        [Fact]
        public void TimeIsUp_FromFolderWithZeroTicks_IsNeverUp()
        {
            // A folder's "Never" inherited through the product page.
            var copy = new TimeIntervalModel(TimeInterval.FromFolder, 0L);

            Assert.False(copy.TimeIsUp(DateTime.MinValue));
        }
    }
}
