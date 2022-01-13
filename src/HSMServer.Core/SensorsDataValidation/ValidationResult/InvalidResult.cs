using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    internal sealed class InvalidResult<T> : ValidationResult<T>
    {
        public InvalidResult(string error) =>
            Errors = new() { error ?? "The input was invalid." };


        public override ResultType ResultType => ResultType.Failed;

        public override List<string> Errors { get; }

        public override T Data => default;


        public override ValidationResult<T> Clone() =>
            new InvalidResult<T>(Error);
    }
}
