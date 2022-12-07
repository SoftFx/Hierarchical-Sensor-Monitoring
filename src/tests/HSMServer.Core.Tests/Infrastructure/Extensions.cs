using HSMCommon.Constants;
using System.Linq;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class StringExtensions
    {
        internal static string GetSensorName(this string path) => path?.Split(CommonConstants.SensorPathSeparator)?[^1];

        internal static string WithoutFirstSlash(this string path) =>
            path.FirstOrDefault() == CommonConstants.SensorPathSeparator ? path[1..] : path;
    }
}
