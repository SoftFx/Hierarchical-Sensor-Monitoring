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
        Error,
    }


    public interface IValidationResult<T>
    {
        public ResultType ResultType { get; }

        public HashSet<string> Errors { get; }

        public T Data { get; }


        public string Error => string.Join(Environment.NewLine, Errors);


        public IValidationResult<T> Clone();
    }
}
