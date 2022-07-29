using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class SensorValuesExstensions
    {
        internal static T FillCommonBarSensorValueProperties<T>(this T sensorValue, string productKey)
        {
            if (sensorValue is BarSensorValueBase barSensorValue)
            {
                barSensorValue.StartTime = DateTime.UtcNow.AddSeconds(-10);
                barSensorValue.EndTime = DateTime.UtcNow.AddSeconds(10);
                barSensorValue.Count = RandomGenerator.GetRandomInt(positive: true);
            }

            return sensorValue.FillCommonSensorValueProperties(productKey);
        }

        internal static T FillCommonSensorValueProperties<T>(this T sensorValue, string productKey, string uniqPath = null)
        {
            if (sensorValue is SensorValueBase sensorValueBase)
            {
                var sensorValueType = typeof(T);

                sensorValueBase.Key = productKey;
                sensorValueBase.Path = $"{sensorValueType}{(string.IsNullOrEmpty(uniqPath) ? string.Empty : $"{uniqPath}")}";
                sensorValueBase.Description = $"{sensorValueType} {nameof(SensorValueBase.Description)}";
                sensorValueBase.Comment = $"{sensorValueType} {nameof(SensorValueBase.Comment)}";
                sensorValueBase.Time = DateTime.UtcNow;
                sensorValueBase.Status = SensorStatus.Ok;
            }

            return sensorValue;
        }
    }
}
