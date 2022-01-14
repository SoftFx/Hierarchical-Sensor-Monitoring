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


    public interface IValidationResult<T>
    {
        public ResultType ResultType { get; }

        public HashSet<string> Errors { get; }

        public T Data { get; }


        public string Error => string.Join(Environment.NewLine, Errors);

        public SensorStatus SensorStatus =>
            ResultType switch
            {
                ResultType.Unknown => SensorStatus.Unknown,
                ResultType.Ok => SensorStatus.Ok,
                ResultType.Warning => SensorStatus.Warning,
                ResultType.Failed => SensorStatus.Error,
                _ => throw new InvalidCastException($"Unknown validation result: {ResultType}"),
            };


        public IValidationResult<T> Clone();
    }
}
