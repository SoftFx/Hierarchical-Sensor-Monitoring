using HSMCommon.Constants;
using System;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class StringExtensions
    {
        internal static string GetSensorName(this string path) => path?.Split(CommonConstants.SensorPathSeparator)?[^1];
    }


    internal static class DateTimeExtension
    {
        internal static long GetTimestamp(this DateTime dateTime)
        {
            var timeSpan = dateTime - DateTime.UnixEpoch;
            return (long)timeSpan.TotalSeconds;
        }
    }
}
