using HSMServer.Core.Model;

namespace HSMServer.Core.Extensions
{
    public static class ValidationResultExtensions
    {
        public static ValidationResult GetWorst(this ValidationResult validationResult, ValidationResult otherResult)
        {
            if (validationResult == ValidationResult.Failed || otherResult == ValidationResult.Failed)
                return ValidationResult.Failed;

            if (validationResult == ValidationResult.OkWithError || otherResult == ValidationResult.OkWithError)
                return ValidationResult.OkWithError;

            return ValidationResult.Ok;
        }
    }
}
