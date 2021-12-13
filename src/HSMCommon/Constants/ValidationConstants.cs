namespace HSMCommon.Constants
{
    public class ValidationConstants
    {
        public const string ObjectIsNull = "Sensor value object is null!";
        public const string PathTooLong = "Path for the sensor is too long.";
        public const string FailedToParseType = "Failed to parse the data type corresponding to the specefied sensor type";
        public const string FailedToCastObject = "Failed to get typed data object.";
        public const string SensorValueIsTooLong = "The value has exceeded the length limit and will be trimmed";
        public const string SensorValueOutdated = "Sensor value is older than ExpectedUpdateInterval!";

        public const int MAX_STRING_LENGTH = 150;
    }
}