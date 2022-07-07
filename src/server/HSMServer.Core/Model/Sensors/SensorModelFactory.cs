using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Model
{
    internal static class SensorModelFactory
    {
        internal static BaseSensorModel Build(SensorEntity entity)
        {
            BaseSensorModel BuildSensor<T>() where T : BaseSensorModel, new() =>
                new T().ApplyEntity(entity);

            return (SensorType)entity.Type switch
            {
                SensorType.Boolean => BuildSensor<BooleanSensorModel>(),
                SensorType.Integer => BuildSensor<IntegerSensorModel>(),
                SensorType.Double => BuildSensor<DoubleSensorModel>(),
                SensorType.String => BuildSensor<StringSensorModel>(),
                SensorType.IntegerBar => BuildSensor<IntegerBarSensorModel>(),
                SensorType.DoubleBar => BuildSensor<DoubleBarSensorModel>(),
                SensorType.File => BuildSensor<FileSensorModel>(),
                _ => throw new ArgumentException($"Unexpected sensor entity type {entity.Type}"),
            };
        }

        internal static BaseSensorModel Build(BaseValue value, SensorEntity entity)
        {
            BaseSensorModel BuildSensor<T>() where T : BaseSensorModel, new() =>
                new T().ApplyEntity(entity);

            return value switch
            {
                BooleanValue => BuildSensor<BooleanSensorModel>(),
                IntegerValue => BuildSensor<IntegerSensorModel>(),
                DoubleValue => BuildSensor<DoubleSensorModel>(),
                StringValue => BuildSensor<StringSensorModel>(),
                IntegerBarValue => BuildSensor<IntegerBarSensorModel>(),
                DoubleBarValue => BuildSensor<DoubleBarSensorModel>(),
                FileValue => BuildSensor<FileSensorModel>(),
                _ => throw new ArgumentException($"Unexpected sensor value type {value.GetType()}"),
            };
        }
    }
}
