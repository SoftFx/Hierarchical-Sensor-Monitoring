using System;
using Xunit;
using HSMServer.Core.Model;


namespace HSMServer.Core.Tests.TreeValuesCacheTests.ModelTests
{
    public class TimeIntervalModelTests
    {
        // A month before 2028-03-01 is 2028-02-01; 31 days of ticks before it is 2028-01-30.
        private static readonly DateTime Now = new(2028, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        public static TheoryData<string, TimeIntervalModel, DateTime?> Policies => new()
        {
            { "forever", TimeIntervalModel.None, null },
            { "forever inherited", TimeIntervalModel.None.ToFromFolderModel(), null },
            { "preset inherited before #1864", new TimeIntervalModel(TimeInterval.FromFolder, 0L), null },
            { "ticks longer than time", new TimeIntervalModel(TimeInterval.Ticks, DateTime.MaxValue.Ticks), null },
            { "own day", new TimeIntervalModel(TimeSpan.FromDays(1).Ticks), new DateTime(2028, 2, 29, 0, 0, 0, DateTimeKind.Utc) },
            { "day inherited", new TimeIntervalModel(TimeSpan.FromDays(1).Ticks).ToFromFolderModel(), new DateTime(2028, 2, 29, 0, 0, 0, DateTimeKind.Utc) },
            { "own month", new TimeIntervalModel(TimeInterval.Month), new DateTime(2028, 2, 1, 0, 0, 0, DateTimeKind.Utc) },
            { "month inherited", new TimeIntervalModel(TimeInterval.Month).ToFromFolderModel(), new DateTime(2028, 2, 1, 0, 0, 0, DateTimeKind.Utc) },
            { "three months inherited", new TimeIntervalModel(TimeInterval.ThreeMonths).ToFromFolderModel(), new DateTime(2027, 12, 1, 0, 0, 0, DateTimeKind.Utc) },
            { "six months inherited", new TimeIntervalModel(TimeInterval.SixMonths).ToFromFolderModel(), new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc) },
            { "year inherited", new TimeIntervalModel(TimeInterval.Year).ToFromFolderModel(), new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc) },
        };

        // The cutoff the retention pass clears to and the history floor of late values and large files take (#1864, #1883).
        [Theory]
        [MemberData(nameof(Policies))]
        public void RetentionCutoff_IsTheCalendarOrTicksCutoff_OrNoneWhenRetentionNeverRemoves(string _, TimeIntervalModel policy, DateTime? expected)
        {
            Assert.Equal(expected, policy.RetentionCutoff(Now));

            // Retention removes history (TimeIsUp of the oldest possible row) exactly when there is a cutoff.
            Assert.Equal(expected is not null, policy.TimeIsUp(DateTime.MinValue));
        }

        //[Theory]
        //[InlineData(TimeInterval.FromParent)]
        //[InlineData(TimeInterval.FromFolder)]
        //[InlineData(TimeInterval.Month)]
        //public void TimeIsUpTests(TimeInterval interval)
        //{

        //    var model = new TimeIntervalModel(interval, TimeSpan.FromDays(30).Ticks);

        //    var result = model.TimeIsUp(DateTime.UtcNow.AddDays(-31));

        //    Assert.True(result);
        //}

    }
}
