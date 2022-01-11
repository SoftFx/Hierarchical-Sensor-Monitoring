using System.Collections.Generic;

namespace HSMServer.Core.SensorsDataValidation
{
    internal sealed class InvalidResult<T> : ValidationResult<T>
    {
        private readonly string _error;


        public InvalidResult(string error) => _error = error;


        public override ResultType ResultType => ResultType.Failed;

        public override List<string> Errors => new() { _error ?? "The input was invalid." };

        public override T Data => default;
    }
}
