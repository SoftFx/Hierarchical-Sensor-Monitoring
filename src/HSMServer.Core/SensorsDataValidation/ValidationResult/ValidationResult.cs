using HSMSensorDataObjects;
using System;
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


        public string Error => string.Join(", ", Errors);

        public SensorStatus SensorStatus =>
            ResultType switch
            {
                ResultType.Unknown => SensorStatus.Unknown,
                ResultType.Ok => SensorStatus.Ok,
                ResultType.Warning => SensorStatus.Warning,
                ResultType.Failed => SensorStatus.Error,
                _ => throw new InvalidCastException($"Unknown validation result: {ResultType}"),
            };
    }
}
