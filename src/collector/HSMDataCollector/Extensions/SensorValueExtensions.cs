using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.Extensions
{
    internal static class SensorValueExtensions
    {
        internal static SensorValueBase Complete(this SensorValueBase sensor,
            string path, SensorStatus status = SensorStatus.Ok, string comment = null)
        {
            sensor.Path = path;
            sensor.Time = DateTime.UtcNow;
            sensor.Status = status;
            sensor.Comment = comment;

            return sensor;
        }
    }
}
