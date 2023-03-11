using HSMServer.Core.Model;

namespace HSMServer.Core
{
    public static class SensorExtensions
    {
        public static bool IsOk(this SensorStatus status)
        {
            return status == SensorStatus.Ok;
        }
    }
}