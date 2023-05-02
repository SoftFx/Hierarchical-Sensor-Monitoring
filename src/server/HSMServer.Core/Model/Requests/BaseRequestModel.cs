using HSMCommon.Constants;
using System;
using System.Linq;

namespace HSMServer.Core.Model.Requests
{
    public abstract class BaseRequestModel
    {
        private const string ErrorTooLongPath = "Path for the sensor is too long.";
        private const string ErrorInvalidPath = "Path has an invalid format.";
        private const string ErrorPathKey = "Path or key is empty.";


        public string Key { get; }

        public string Path { get; }

        public Guid KeyGuid { get; }

        public string[] PathParts { get; }


        public BaseRequestModel(string key, string path)
        {
            Key = key;
            Path = path;

            if (Guid.TryParse(Key, out var guid))
                KeyGuid = guid;

            PathParts = GetPathParts(Path);
        }


        public bool TryCheckRequest(out string message)
        {
            if (string.IsNullOrEmpty(Key) || string.IsNullOrEmpty(Path))
            {
                message = ErrorPathKey;
                return false;
            }

            if (PathParts.Contains(string.Empty) || Path.Contains('\\'))
            {
                message = ErrorInvalidPath;
                return false;
            }
            else if (PathParts.Length > ConfigurationConstants.DefaultMaxPathLength) // TODO : get maxPathLength from IConfigurationProvider
            {
                message = ErrorTooLongPath;
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static string[] GetPathParts(string path)
        {
            path = path.FirstOrDefault() == CommonConstants.SensorPathSeparator ? path[1..] : path;

            return path.Split(CommonConstants.SensorPathSeparator, StringSplitOptions.TrimEntries);
        }
    }
}
