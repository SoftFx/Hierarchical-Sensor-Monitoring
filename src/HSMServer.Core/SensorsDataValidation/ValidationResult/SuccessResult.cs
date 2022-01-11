using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    internal sealed class SuccessResult<T> : ValidationResult<T>
    {
        private readonly T _data;


        public SuccessResult(T data) => _data = data;


        public override ResultType ResultType => ResultType.Ok;

        public override List<string> Errors => new();

        public override T Data => _data;
    }
}
