using System;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Extensions
{
    internal static class SensorValueExtensions
    {
        private const int MaxSensorValueCommentLength = 1024;

        internal static bool IsValidStatus(SensorStatus status) => Enum.IsDefined(typeof(SensorStatus), status);

        internal static bool IsValidValue<T>(T value, SensorStatus status) =>
            IsValidStatus(status) && IsSupportedValue(value);

        internal static void ThrowIfUnsupportedValue<T>(T value)
        {
            if (!IsSupportedValue(value))
                throw new ArgumentException($"Unsupported sensor value '{value}'.", nameof(value));
        }

        private static bool IsSupportedValue<T>(T value)
        {
            if (value is double doubleValue)
                return !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue);

            return true;
        }

        internal static SensorValueBase Complete(this SensorValueBase sensor, string comment = null, SensorStatus status = SensorStatus.Ok)
        {
            sensor.Time = DateTime.UtcNow;
            sensor.Status = status;
            sensor.Comment = comment;

            return sensor;
        }

        internal static SensorValueBase TrimLongComment(this SensorValueBase value)
        {
            if (value?.Comment != null && value.Comment.Length > MaxSensorValueCommentLength)
                value.Comment = value.Comment.Substring(0, MaxSensorValueCommentLength);

            return value;
        }
    }
}
