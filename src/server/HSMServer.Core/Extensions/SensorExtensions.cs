﻿using HSMServer.Core.Model;

namespace HSMServer.Core
{
    public static class SensorExtensions
    {
        public static bool IsBar(this SensorType type) => type is SensorType.IntegerBar or SensorType.DoubleBar;

        public static bool IsOk(this SensorStatus status) => status == SensorStatus.Ok;

        public static bool IsCustom(this OldTimeInterval interval) => interval == OldTimeInterval.Custom;

        public static bool UseCustomPeriod(this OldTimeInterval interval) => interval is OldTimeInterval.Custom or OldTimeInterval.FromFolder;

        public static string ToIcon(this SensorStatus status) => status switch
        {
            SensorStatus.Ok => "✅",
            SensorStatus.Warning => "⚠️",
            SensorStatus.Error => "❌",
            SensorStatus.OffTime => "💤",
            _ => "❓"
        };

        public static bool HasGrafana(this Integration integration) => integration.HasFlag(Integration.Grafana);
    }
}