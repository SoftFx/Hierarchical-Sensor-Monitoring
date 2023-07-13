﻿using HSMServer.Model;
using System;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Extensions
{
    public static class TimeIntervalExtensions
    {
        public static bool IsParent(this TimeInterval interval) => interval is TimeInterval.FromParent;

        public static bool IsCustom(this TimeInterval interval) => interval is TimeInterval.Custom;

        public static bool IsUnset(this TimeInterval interval) => interval is TimeInterval.Forever or TimeInterval.None;

        public static bool IsStatic(this TimeInterval interval) => interval is >= TimeInterval.OneMinute and < TimeInterval.Month;

        public static bool IsDynamic(this TimeInterval interval) => interval is >= TimeInterval.Month and <= TimeInterval.Year;

        public static bool IsDefined(this TimeInterval _, long ticks) => Enum.IsDefined(typeof(TimeInterval), ticks);


        public static TimeInterval ToDynamicServer(this CoreTimeInterval core) => core switch
        {
            CoreTimeInterval.Month => TimeInterval.Month,
            CoreTimeInterval.ThreeMonths => TimeInterval.ThreeMonths,
            CoreTimeInterval.SixMonths => TimeInterval.SixMonths,
            CoreTimeInterval.Year => TimeInterval.Year,

            CoreTimeInterval.FromParent => TimeInterval.FromParent,
            CoreTimeInterval.None => TimeInterval.None,

            _ => throw new NotImplementedException(),
        };

        public static CoreTimeInterval ToDynamicCore(this TimeInterval server) => server switch
        {
            TimeInterval.Month => CoreTimeInterval.Month,
            TimeInterval.ThreeMonths => CoreTimeInterval.ThreeMonths,
            TimeInterval.SixMonths => CoreTimeInterval.SixMonths,
            TimeInterval.Year => CoreTimeInterval.Year,

            _ => throw new NotImplementedException(),
        };
    }
}
