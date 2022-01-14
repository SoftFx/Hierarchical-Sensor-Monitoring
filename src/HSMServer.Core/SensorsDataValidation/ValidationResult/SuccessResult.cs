using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    internal readonly struct SuccessResult<T> : IValidationResult<T>
    {
        public SuccessResult(T data) => Data = data;


        public ResultType ResultType => ResultType.Ok;

        public HashSet<string> Errors => new();

        public T Data { get; }


        public IValidationResult<T> Clone() =>
            new SuccessResult<T>(Data);
    }
}
