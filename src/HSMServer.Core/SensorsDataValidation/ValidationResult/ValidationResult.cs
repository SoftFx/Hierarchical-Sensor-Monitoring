using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    public enum ResultType
    {
        Unknown = 0,
        Ok,
        Warning,
        Failed,
    }


    public abstract class ValidationResult<T>
    {
        public abstract ResultType ResultType { get; }

        public abstract List<string> Errors { get; }

        public abstract T Data { get; }
    }
}
