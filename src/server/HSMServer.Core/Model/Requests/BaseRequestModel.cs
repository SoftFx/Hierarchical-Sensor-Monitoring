using HSMCommon.Constants;
using HSMServer.Core.Extensions;
using System;
using System.Linq;

namespace HSMServer.Core.Model.Requests
{
    public abstract class BaseRequestModel
    {
        private const string ErrorInvalidPath = "Path has an invalid format.";
        private const string ErrorTooLongPath = "Path for the sensor is too long.";


        public string Key { get; init; }

        public string Path { get; init; }


        internal bool IsEmpty => string.IsNullOrEmpty(Key) || string.IsNullOrEmpty(Path);


        internal void Deconstruct(out string key, out string path)
        {
            key = Key;
            path = Path;
        }

        internal bool TryCheckPath(out string[] parts, out string message)
        {
            parts = Path.GetParts();
            message = string.Empty;

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

            return true;
        }
    }
}
