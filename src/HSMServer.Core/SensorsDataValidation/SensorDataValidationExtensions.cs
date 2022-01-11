using HSMCommon.Constants;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Configuration;

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
            var pathLength = value.Path.Split(CommonConstants.SensorPathSeparator).Length;

            return pathLength > _maxPathLength
                ? new InvalidResult<SensorValueBase>(ValidationConstants.PathTooLong)
                : new SuccessResult<SensorValueBase>(value);
        }
    }
}
