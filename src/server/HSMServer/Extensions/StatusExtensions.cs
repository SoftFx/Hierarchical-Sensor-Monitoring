using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModels;

namespace HSMServer.Extensions
{
    internal static class StatusExtensions
    {
        internal static SensorStatusWeb ToWebStatus(this SensorStatus status) =>
            status switch
            {
                SensorStatus.Ok => SensorStatusWeb.Ok,
                SensorStatus.Warning => SensorStatusWeb.Warning,
                SensorStatus.Error => SensorStatusWeb.Error,
                SensorStatus.OffTime => SensorStatusWeb.OffTime,
                _ => SensorStatusWeb.Ok,
            };

        internal static SensorStatus ToCoreStatus(this SensorStatusWeb status) =>
            status switch
            {
                SensorStatusWeb.Ok => SensorStatus.Ok,
                SensorStatusWeb.Warning => SensorStatus.Warning,
                SensorStatusWeb.Error => SensorStatus.Error,
                SensorStatusWeb.OffTime => SensorStatus.OffTime,
                _ => SensorStatus.Ok,
            };
    }
}
