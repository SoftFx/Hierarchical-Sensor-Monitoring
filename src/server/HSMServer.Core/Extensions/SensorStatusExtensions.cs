using HSMSensorDataObjects;

namespace HSMServer.Core.Extensions
{
    public static class SensorStatusExtensions
    {
        public static SensorStatus GetWorst(this SensorStatus sensorStatus, SensorStatus otherStatus)
        {
            if (sensorStatus == SensorStatus.Error || otherStatus == SensorStatus.Error)
                return SensorStatus.Error;

            if (sensorStatus == SensorStatus.Warning || otherStatus == SensorStatus.Warning)
                return SensorStatus.Warning;

            return SensorStatus.Ok;
        }
    }
}
