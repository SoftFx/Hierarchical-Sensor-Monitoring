using HSMCommon.Constants;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class StringExtensions
    {
        internal static string GetSensorName(this string path) => path?.Split(CommonConstants.SensorPathSeparator)?[^1];
    }
}
