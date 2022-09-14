using HSMServer.Core.Model;

namespace HSMServer.Extensions
{
    public static class SensorExtensions
    {
        public static string ToCssIconClass(this SensorStatus status) =>
            status switch
            {
                SensorStatus.Ok => "tree-icon-ok",
                SensorStatus.Warning => "tree-icon-warning",
                SensorStatus.Error => "tree-icon-error",
                _ => "tree-icon-unknown",
            };

        public static string ToIcon(this SensorStatus status) =>
            $"fas fa-circle {status.ToCssIconClass()}";

        public static string ToCssClass(this SensorState state) =>
            state switch
            {
                SensorState.Blocked => "blockedSensor-span",
                _ => string.Empty,
            };
    }
}
