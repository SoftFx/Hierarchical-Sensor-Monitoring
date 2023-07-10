using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using System;
using System.Text.Json.Nodes;

namespace HSMServer.Core
{
    [Obsolete]
    public static class Migrators
    {
        public static TimeIntervalModel ToNewInterval(TimeIntervalEntity old)
        {
            var oldEnum = (OldTimeInterval)old.Interval;

            var newTicks = oldEnum switch
            {
                OldTimeInterval.OneMinute => 600_000_000L,
                OldTimeInterval.FiveMinutes => 3_000_000_000L,
                OldTimeInterval.TenMinutes => 6_000_000_000L,

                OldTimeInterval.Hour => 36_000_000_000L,
                OldTimeInterval.Day => 864_000_000_000L,
                OldTimeInterval.Week => 6_048_000_000_000L,

                _ => old.Ticks,
            };

            var newEnum = oldEnum switch
            {
                OldTimeInterval.TenMinutes or OldTimeInterval.Hour or OldTimeInterval.Day or OldTimeInterval.Week
                or OldTimeInterval.OneMinute or OldTimeInterval.FiveMinutes or OldTimeInterval.Custom => TimeInterval.Ticks,


                OldTimeInterval.Month => TimeInterval.Month,
                OldTimeInterval.ThreeMonths => TimeInterval.ThreeMonths,
                OldTimeInterval.SixMonths => TimeInterval.SixMonths,
                OldTimeInterval.Year => TimeInterval.Year,


                OldTimeInterval.FromFolder => TimeInterval.FromFolder,
                OldTimeInterval.FromParent => TimeInterval.FromParent,
                _ => throw new NotImplementedException(),
            };

            if (newEnum == TimeInterval.Ticks && newTicks == 0L)
                newEnum = TimeInterval.None;

            return new TimeIntervalModel(newEnum, newTicks);
        }
    }
}
