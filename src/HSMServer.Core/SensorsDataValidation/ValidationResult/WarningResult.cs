using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    internal sealed class WarningResult<T> : ValidationResult<T>
    {
        public WarningResult(T data, string error)
        {
            Data = data;
            Errors = new() { error ?? "The input has some errors." };
        }


        public override ResultType ResultType => ResultType.Warning;

        public override List<string> Errors { get; }

        public override T Data { get; }


        public override ValidationResult<T> Clone() =>
            new WarningResult<T>(Data, Error);
    }
}
