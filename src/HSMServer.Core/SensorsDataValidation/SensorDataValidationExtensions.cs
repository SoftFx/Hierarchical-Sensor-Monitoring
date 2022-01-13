using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Configuration;
using System;

namespace HSMServer.Core.SensorsDataValidation
{
    public static class SensorDataValidationExtensions
    {
        private static int _maxPathLength;


        public static void Initialize(IConfigurationProvider configurationProvider)
        {
            var pathLengthObject = configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.MaxPathLength);

            if (int.TryParse(pathLengthObject.Value, out var maxPathLength))
                _maxPathLength = maxPathLength;
        }


        public static ValidationResult<SensorValueBase> Validate(this SensorValueBase value)
        {
            if (value == null)
                return new InvalidResult<SensorValueBase>(ValidationConstants.ObjectIsNull);

            return CombineValidationResults(value.TypedValidate(), value.ValidateSensorPath());
        }


        private static ValidationResult<T> CombineValidationResults<T>(params ValidationResult<T>[] validationResults)
        {
            Array.Sort(validationResults, (result1, result2) => -result1.ResultType.CompareTo(result2.ResultType));

            var worstValidationResult = validationResults[0].Clone();

            if (worstValidationResult.ResultType == ResultType.Ok)
                return worstValidationResult;

            foreach (var validationResult in validationResults)
                if (!string.IsNullOrEmpty(validationResult.Error) && !worstValidationResult.Errors.Contains(validationResult.Error))
                    worstValidationResult.Errors.Add(validationResult.Error);

            return worstValidationResult;
        }

        private static ValidationResult<SensorValueBase> ValidateSensorPath(this SensorValueBase value)
        {
            var pathLength = value.Path.Split(CommonConstants.SensorPathSeparator).Length;

            return pathLength > _maxPathLength
                ? new InvalidResult<SensorValueBase>(ValidationConstants.PathTooLong)
                : new SuccessResult<SensorValueBase>(value);
        }

        private static ValidationResult<SensorValueBase> TypedValidate(this SensorValueBase value) =>
            value switch
            {
                StringSensorValue stringSensorValue => stringSensorValue.Validate(),
                UnitedSensorValue unitedSensorValue => CombineValidationResults(unitedSensorValue.ValidateUnitedSensorData(), unitedSensorValue.ValidateUnitedSensorType()),
                _ => new SuccessResult<SensorValueBase>(value),
            };

        private static ValidationResult<SensorValueBase> Validate(this StringSensorValue value)
        {
            if (value.StringValue.Length > ValidationConstants.MAX_STRING_LENGTH)
            {
                value.StringValue = value.StringValue.Substring(0, ValidationConstants.MAX_STRING_LENGTH);
                return new WarningResult<SensorValueBase>(value, ValidationConstants.SensorValueIsTooLong);
            }

            return new SuccessResult<SensorValueBase>(value);
        }

        private static ValidationResult<SensorValueBase> ValidateUnitedSensorData(this UnitedSensorValue value)
        {
            if (value.Data.Length > ValidationConstants.MaxUnitedSensorDataLength)
            {
                value.Data = value.Data.Substring(0, ValidationConstants.MaxUnitedSensorDataLength);
                return new WarningResult<SensorValueBase>(value, ValidationConstants.SensorValueIsTooLong);
            }

            return new SuccessResult<SensorValueBase>(value);
        }

        private static ValidationResult<SensorValueBase> ValidateUnitedSensorType(this UnitedSensorValue value) =>
            value.Type switch
            {
                SensorType.BooleanSensor or
                SensorType.IntSensor or
                SensorType.DoubleSensor or
                SensorType.StringSensor or
                SensorType.IntegerBarSensor or
                SensorType.DoubleBarSensor => new SuccessResult<SensorValueBase>(value),
                _ => new InvalidResult<SensorValueBase>(ValidationConstants.FailedToParseType),
            };
    }
}
