using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Configuration;

namespace HSMServer.Core.SensorsDataValidation
{
    public static class SensorDataValidationExtensions
    {
        private static int _maxPathLength = ConfigurationConstants.DefaultMaxPathLength;


        public static void Initialize(IConfigurationProvider configurationProvider)
        {
            var pathLengthObject = configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.MaxPathLength);

            if (int.TryParse(pathLengthObject.Value, out var maxPathLength))
                _maxPathLength = maxPathLength;
        }


        public static ValidationResult Validate(this SensorValueBase value)
        {
            if (value == null)
                return PredefinedValidationResults.NullObjectValidationResult;

            return value.TypedValidate() + value.ValidateSensorPath();
        }


        private static ValidationResult ValidateSensorPath(this SensorValueBase value)
        {
            var pathLength = value.Path.Split(CommonConstants.SensorPathSeparator).Length;

            return pathLength > _maxPathLength
                ? PredefinedValidationResults.TooLongPathValidationResult
                : new ValidationResult(value);
        }

        private static ValidationResult TypedValidate(this SensorValueBase value) =>
            value switch
            {
                StringSensorValue stringSensorValue => stringSensorValue.Validate(),
                UnitedSensorValue unitedSensorValue => unitedSensorValue.ValidateUnitedSensorData() + unitedSensorValue.ValidateUnitedSensorType(),
                _ => new ValidationResult(value),
            };

        private static ValidationResult Validate(this StringSensorValue value)
        {
            if (value.StringValue.Length > ValidationConstants.MAX_STRING_LENGTH)
            {
                value.StringValue = value.StringValue[0..ValidationConstants.MAX_STRING_LENGTH];
                return PredefinedValidationResults.GetTooLongSensorValueValidationResult(value);
            }

            return new ValidationResult(value);
        }

        private static ValidationResult ValidateUnitedSensorData(this UnitedSensorValue value)
        {
            if (value.Data.Length > ValidationConstants.MaxUnitedSensorDataLength)
            {
                value.Data = value.Data[0..ValidationConstants.MaxUnitedSensorDataLength];
                return PredefinedValidationResults.GetTooLongSensorValueValidationResult(value);
            }

            return new ValidationResult(value);
        }

        private static ValidationResult ValidateUnitedSensorType(this UnitedSensorValue value) =>
            value.Type switch
            {
                SensorType.BooleanSensor or
                SensorType.IntSensor or
                SensorType.DoubleSensor or
                SensorType.StringSensor or
                SensorType.IntegerBarSensor or
                SensorType.DoubleBarSensor => new ValidationResult(value),
                _ => PredefinedValidationResults.IncorrectTypeValidationResult,
            };
    }
}
