using System;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    internal static class SensorValuesExstensions
    {
        internal static T FillCommonBarSensorValueProperties<T>(this T sensorValue, string productKey)
        {
            if (sensorValue is BarSensorValueBase barSensorValue)
            {
                barSensorValue.StartTime = DateTime.UtcNow.AddSeconds(-10);
                barSensorValue.EndTime = DateTime.UtcNow.AddSeconds(10);
                barSensorValue.Count = RandomValuesGenerator.GetRandomInt(positive: true);
            }

            return sensorValue.FillCommonSensorValueProperties(productKey);
        }

        internal static T FillCommonSensorValueProperties<T>(this T sensorValue, string productKey)
        {
            if (sensorValue is SensorValueBase sensorValueBase)
            {
                var sensorValueType = typeof(T);

                sensorValueBase.Key = productKey;
                sensorValueBase.Path = $"{sensorValueType}";
                sensorValueBase.Description = $"{sensorValueType} {nameof(SensorValueBase.Description)}";
                sensorValueBase.Comment = $"{sensorValueType} {nameof(SensorValueBase.Comment)}";
                sensorValueBase.Time = DateTime.UtcNow;
                sensorValueBase.Status = SensorStatus.Ok;
            }

            return sensorValue;
        }
    }
}
