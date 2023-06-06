using Client = HSMServer.Model.TreeViewModel;
using Server = HSMServer.Core.Model;

namespace HSMServer.Extensions
{
    internal static class StatusExtensions
    {
        internal static Client.SensorStatus ToClient(this Server.SensorResult? result)
        {
            return result?.Status.ToClient() ?? Client.SensorStatus.Empty;
        }

        internal static Client.SensorStatus ToClient(this Server.SensorStatus status) =>
            status switch
            {
                Server.SensorStatus.Ok => Client.SensorStatus.Ok,
                Server.SensorStatus.Warning => Client.SensorStatus.Warning,
                Server.SensorStatus.Error => Client.SensorStatus.Error,
                Server.SensorStatus.OffTime => Client.SensorStatus.OffTime,
                _ => Client.SensorStatus.Ok,
            };

        internal static Server.SensorStatus ToCore(this Client.SensorStatus status) =>
            status switch
            {
                Client.SensorStatus.Ok => Server.SensorStatus.Ok,
                Client.SensorStatus.Warning => Server.SensorStatus.Warning,
                Client.SensorStatus.Error => Server.SensorStatus.Error,
                Client.SensorStatus.OffTime => Server.SensorStatus.OffTime,
                _ => Server.SensorStatus.Ok,
            };


        public static string ToSelectIcon(this Client.SensorStatus status) => status switch
        {
            Client.SensorStatus.Ok => "🟢",
            Client.SensorStatus.Warning => "️🟡",
            Client.SensorStatus.Error => "🔴",
            Client.SensorStatus.OffTime => "⚪️",
        };
    }
}
