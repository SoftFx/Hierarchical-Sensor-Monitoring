using HSMServer.Core.Model;

namespace HSMServer.Extensions
{
    public static class SensorStateExtensions
    {
        public static string ToCssClass(this SensorState state) =>
            state switch
            {
                SensorState.Blocked => "blockedSensor-span",
                _ => string.Empty,
            };
    }
}
