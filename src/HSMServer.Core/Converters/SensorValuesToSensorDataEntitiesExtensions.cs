using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Extensions;

namespace HSMServer.Core.Converters
{
    public static class SensorValuesToSensorDataEntitiesExtensions
    {
        public static SensorDataEntity Convert(this SensorValueBase sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown) =>
            new()
            {
                Path = sensorValue.Path,
                Status = (byte)sensorValue.Status.GetWorst(validationStatus),
                Time = sensorValue.Time.ToUniversalTime(),
                TimeCollected = timeCollected.ToUniversalTime(),
                Timestamp = GetTimestamp(sensorValue.Time),
                TypedData = TypedDataFactory.GetTypedData(sensorValue),
                DataType = (byte)SensorTypeFactory.GetSensorType(sensorValue),
            };


        private static long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = dateTime - DateTime.UnixEpoch;
            return (long)timeSpan.TotalSeconds;
        }
    }
}
