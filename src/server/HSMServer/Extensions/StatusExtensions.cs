using ClientSensorStatus = HSMServer.Model.TreeViewModels.SensorStatus;
using CoreSensorStatus = HSMServer.Core.Model.SensorStatus;

namespace HSMServer.Extensions
{
    internal static class StatusExtensions
    {
        internal static ClientSensorStatus ToClient(this CoreSensorStatus status) =>
            status switch
            {
                CoreSensorStatus.Ok => ClientSensorStatus.Ok,
                CoreSensorStatus.Warning => ClientSensorStatus.Warning,
                CoreSensorStatus.Error => ClientSensorStatus.Error,
                CoreSensorStatus.OffTime => ClientSensorStatus.OffTime,
                _ => ClientSensorStatus.Ok,
            };

        internal static CoreSensorStatus ToCore(this ClientSensorStatus status) =>
            status switch
            {
                ClientSensorStatus.Ok => CoreSensorStatus.Ok,
                ClientSensorStatus.Warning => CoreSensorStatus.Warning,
                ClientSensorStatus.Error => CoreSensorStatus.Error,
                ClientSensorStatus.OffTime => CoreSensorStatus.OffTime,
                _ => CoreSensorStatus.Ok,
            };
    }
}
