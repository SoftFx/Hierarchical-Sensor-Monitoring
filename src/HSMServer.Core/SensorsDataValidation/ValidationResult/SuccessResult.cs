using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    internal sealed class SuccessResult<T> : ValidationResult<T>
    {
        public SuccessResult(T data) => Data = data;


        public override ResultType ResultType => ResultType.Ok;

        public override List<string> Errors => new();

        public override T Data { get; }


        public override ValidationResult<T> Clone() =>
            new SuccessResult<T>(Data);
    }
}
