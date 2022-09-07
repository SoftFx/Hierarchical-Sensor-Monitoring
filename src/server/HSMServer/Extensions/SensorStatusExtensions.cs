using HSMServer.Core.Model;

namespace HSMServer.Extensions
{
    public static class SensorStatusExtensions
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
    }
}
