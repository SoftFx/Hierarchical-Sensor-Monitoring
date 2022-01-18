using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    internal readonly struct InvalidResult<T> : IValidationResult<T>
    {
        public InvalidResult(string error) =>
            Errors = new() { error ?? "The input was invalid." };

        public InvalidResult(HashSet<string> errors) => Errors = new(errors);


        public ResultType ResultType => ResultType.Error;

        public HashSet<string> Errors { get; }

        public T Data => default;


        public IValidationResult<T> Clone() =>
            new InvalidResult<T>(Errors);
    }
}
