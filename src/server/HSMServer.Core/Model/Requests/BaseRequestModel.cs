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
        private const int MaxPathLength = 10;

        private readonly bool _failKey;


        public string[] PathParts { get; }

        public string Path { get; }

        public Guid Key { get; }


        public string SensorName => PathParts[^1];


        public BaseRequestModel(Guid key, string path)
        {
            Key = key;

            PathParts = GetPathParts(path);

            Path = string.Join(CommonConstants.SensorPathSeparator, PathParts);
        }

        public BaseRequestModel(string key, string path)
        {
            _failKey = !Guid.TryParse(key, out var guid);

            if (!_failKey)
                Key = guid;

            PathParts = GetPathParts(path);

            Path = string.Join(CommonConstants.SensorPathSeparator, PathParts);
        }


        public bool TryCheckRequest(out string message)
        {
            if (_failKey || string.IsNullOrEmpty(Path))
            {
                message = ErrorPathKey;
                return false;
            }

            if (PathParts.Contains(string.Empty) || Path.Contains('\\') || Path.Contains('\t'))
            {
                message = ErrorInvalidPath;
                return false;
            }
            else if (PathParts.Length > MaxPathLength) // TODO : get maxPathLength from IServerConfig
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

            return path.Split(CommonConstants.SensorPathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
