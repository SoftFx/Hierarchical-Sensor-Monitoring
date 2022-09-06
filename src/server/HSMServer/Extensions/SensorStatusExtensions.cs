using HSMServer.Core.Model;

namespace HSMServer.Extensions
{
    public static class SensorStatusExtensions
    {
        public static string ToIconClass(this SensorStatus status) =>
            status switch
            {
                SensorStatus.Unknown => "tree-icon-unknown",
                SensorStatus.Ok => "tree-icon-ok",
                SensorStatus.Warning => "tree-icon-warning",
                SensorStatus.Error => "tree-icon-error",
                _ => "tree-icon-unknown",
            };
    }
}
