using HSMCommon.Constants;
using HSMSensorDataObjects.FullDataObject;

namespace HSMServer.Core.SensorsDataValidation
{
    internal static class TypicalValidationResults
    {
        internal static ValidationResult NullObjectValidationResult { get; } =
            new(ValidationConstants.ObjectIsNull);

        internal static ValidationResult TooLongPathValidationResult { get; } =
            new(ValidationConstants.PathTooLong);

        internal static ValidationResult IncorrectTypeValidationResult { get; } =
            new(ValidationConstants.FailedToParseType);


        internal static ValidationResult GetTooLongSensorValueValidationResult(SensorValueBase value) =>
            new(value, ValidationConstants.SensorValueIsTooLong);
    }
}
