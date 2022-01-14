using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    internal readonly struct WarningResult<T> : IValidationResult<T>
    {
        public WarningResult(T data) : this() => Data = data;

        public WarningResult(T data, string error) : this(data) =>
            Errors = new() { error ?? "The input has some errors." };

        public WarningResult(T data, HashSet<string> errors) : this(data) =>
            Errors = new(errors);


        public ResultType ResultType => ResultType.Warning;

        public HashSet<string> Errors { get; }

        public T Data { get; }


        public IValidationResult<T> Clone() =>
            new WarningResult<T>(Data, Errors);
    }
}
