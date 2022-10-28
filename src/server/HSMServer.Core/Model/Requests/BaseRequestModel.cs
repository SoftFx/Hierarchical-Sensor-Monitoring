using HSMCommon.Constants;
using System;
using System.Linq;

namespace HSMServer.Core.Model.Requests
{
    public abstract class BaseRequestModel
    {
        private const string ErrorPathKey = "Path or key is empty.";
        private const string ErrorInvalidPath = "Path has an invalid format.";
        private const string ErrorTooLongPath = "Path for the sensor is too long.";


        public string Key { get; init; }

        public string Path { get; init; }


        internal void Deconstruct(out string key, out string path)
        {
            key = Key;
            path = Path;
        }

        public bool TryCheckRequest(out string message)
        {
            if (string.IsNullOrEmpty(Key) || string.IsNullOrEmpty(Path))
            {
                message = ErrorPathKey;
                return false;
            }

            var parts = GetPathParts();
            if (parts.Contains(string.Empty) || Path.Contains('\\'))
            {
                message = ErrorInvalidPath;
                return false;
            }
            else if (parts.Length > ConfigurationConstants.DefaultMaxPathLength) // TODO : get maxPathLength from IConfigurationProvider
            {
                message = ErrorTooLongPath;
                return false;
            }

            message = string.Empty;
            return true;
        }

        internal string[] GetPathParts()
        {
            var path = GetPathWithoutStartSeparator(Path);

            return path.Split(CommonConstants.SensorPathSeparator, StringSplitOptions.TrimEntries);
        }

        private static string GetPathWithoutStartSeparator(string path) =>
            path[0] == CommonConstants.SensorPathSeparator ? path.Remove(0, 1) : path;
    }
}
