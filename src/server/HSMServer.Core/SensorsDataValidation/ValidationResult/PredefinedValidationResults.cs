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


        internal static ValidationResult GetTooLongSensorValueValidationResult() =>
            new(ValidationConstants.SensorValueIsTooLong, SensorStatus.Warning);

        internal static ValidationResult Success { get; } = new();

        internal static ValidationResult OutdatedSensor { get; }
            = new(ValidationConstants.SensorValueOutdated, SensorStatus.Warning);

        internal static ValidationResult TooLongStringSensor { get; } =
            new(ValidationConstants.SensorValueIsTooLong, SensorStatus.Warning);
    }
}
