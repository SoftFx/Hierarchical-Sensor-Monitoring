using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.Extensions
{
    internal static class SensorValueExtensions
    {
        private const int MaxSensorValueCommentLength = 1024;


        internal static SensorValueBase Complete(this SensorValueBase sensor,
            string path, string comment = null, SensorStatus status = SensorStatus.Ok)
        {
            sensor.Path = path;
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
