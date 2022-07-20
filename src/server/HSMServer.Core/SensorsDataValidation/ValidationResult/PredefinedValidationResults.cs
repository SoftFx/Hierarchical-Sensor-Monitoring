using HSMCommon.Constants;
using HSMServer.Core.Model;

namespace HSMServer.Core.SensorsDataValidation
{
    internal static class PredefinedValidationResults
    {
        internal static ValidationResult NullObjectValidationResult { get; } =
            new(ValidationConstants.ObjectIsNull, SensorStatus.Error);

        internal static ValidationResult TooLongPathValidationResult { get; } =
            new(ValidationConstants.PathTooLong, SensorStatus.Error);

        internal static ValidationResult IncorrectTypeValidationResult { get; } =
            new(ValidationConstants.FailedToParseType, SensorStatus.Error);
    }
}
