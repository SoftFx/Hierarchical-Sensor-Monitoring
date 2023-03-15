using HSMServer.Core.Model;

namespace HSMServer.Core
{
    public static class SensorExtensions
    {
        public static bool IsOk(this SensorStatus status)
        {
            return status == SensorStatus.Ok;
        }


        public static bool IsCustom(this TimeInterval interval)
        {
            return interval == TimeInterval.Custom;
        }

        public static string ToIcon(this SensorStatus status) => status switch
        {
            SensorStatus.Ok => "✅",
            SensorStatus.Warning => "⚠️",
            SensorStatus.Error => "❌",
            SensorStatus.OffTime => "⏸",
            _ => "❓"
        };
    }
}