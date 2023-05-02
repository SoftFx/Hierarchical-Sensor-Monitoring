using HSMServer.Core.Model;

namespace HSMServer.Core
{
    public static class SensorExtensions
    {
        public static bool IsOk(this SensorStatus status) => status == SensorStatus.Ok;

        public static bool IsCustom(this TimeInterval interval) => interval == TimeInterval.Custom;

        public static bool UseCustomPeriod(this TimeInterval interval) => interval is TimeInterval.Custom or TimeInterval.FromFolder;

        public static string ToIcon(this SensorStatus status) => status switch
        {
            SensorStatus.Ok => "✅",
            SensorStatus.Warning => "⚠️",
            SensorStatus.Error => "❌",
            SensorStatus.OffTime => "💤",
            _ => "❓"
        };
    }
}