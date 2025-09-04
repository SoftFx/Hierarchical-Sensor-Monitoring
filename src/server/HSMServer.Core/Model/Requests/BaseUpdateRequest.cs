using System;
using System.Linq;
using HSMCommon.Constants;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    public abstract record BaseUpdateRequest : IUpdateRequest
    {
        private const string ErrorTooLongPath = "Path for the sensor is too long.";
        private const string ErrorInvalidPath = "Path has an invalid format.";
        private const string ErrorPathKey = "Path or key is empty.";
        private const int MaxPathLength = 10;

        public string[] PathParts { get; }

        public string Path { get; }

        public string SensorName => PathParts[^1];

        public Guid ProductId { get; }

        public BaseUpdateRequest(Guid productId, string path)
        {
            ProductId = productId;

            PathParts = GetPathParts(path);

            Path = string.Join(CommonConstants.SensorPathSeparator, PathParts);
        }


        public bool TryCheckRequest(out string message)
        {
            if (ProductId == Guid.Empty || string.IsNullOrEmpty(Path))
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
