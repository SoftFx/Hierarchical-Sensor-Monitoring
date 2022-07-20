namespace HSMCommon.Constants
{
    public class ValidationConstants
    {
        public const string ObjectIsNull = "Sensor value object is null!";
        public const string PathTooLong = "Path for the sensor is too long.";
        public const string FailedToParseType = "Failed to parse the data type corresponding to the specefied sensor type";
        public const string FailedToCastObject = "Failed to get typed data object.";

        public const int MAX_STRING_LENGTH = 150;
        public const int MaxUnitedSensorDataLength = 1024;
    }
}