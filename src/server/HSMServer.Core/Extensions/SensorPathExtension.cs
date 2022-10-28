using HSMCommon.Constants;
using System;

namespace HSMServer.Core.Extensions
{
    internal static class SensorPathExtension
    {
        internal static string[] GetParts(this string path)
        {
            path = GetPathWithoutStartSeparator(path);

            return path.Split(CommonConstants.SensorPathSeparator, StringSplitOptions.TrimEntries);
        }

        private static string GetPathWithoutStartSeparator(string path) =>
            path[0] == CommonConstants.SensorPathSeparator ? path.Remove(0, 1) : path;
    }
}
