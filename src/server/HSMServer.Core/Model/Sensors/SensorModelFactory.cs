using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Sensors.SensorModels;
using HSMCommon.Model;
using HSMServer.Core.DataLayer;
using System.Runtime.CompilerServices;
using HSMServer.Core.Schedule;

namespace HSMServer.Core.Model
{
    internal static class SensorModelFactory
    {
        internal static BaseSensorModel Build(SensorEntity entity, IDatabaseCore database, IAlertScheduleProvider alertScheduleProvider)
        {
            return (SensorType)entity.Type switch
            {
                SensorType.Boolean => new BooleanSensorModel(entity, database, alertScheduleProvider),
                SensorType.Integer => new IntegerSensorModel(entity, database, alertScheduleProvider),
                SensorType.Double => new DoubleSensorModel(entity, database, alertScheduleProvider),
                SensorType.String => new StringSensorModel(entity, database, alertScheduleProvider),
                SensorType.IntegerBar => new IntegerBarSensorModel(entity, database, alertScheduleProvider),
                SensorType.DoubleBar => new DoubleBarSensorModel(entity, database, alertScheduleProvider),
                SensorType.File => new FileSensorModel(entity, database, alertScheduleProvider),
                SensorType.TimeSpan => new TimeSpanSensorModel(entity, database, alertScheduleProvider),
                SensorType.Version => new VersionSensorModel(entity, database, alertScheduleProvider),
                SensorType.Rate => new RateSensorModel(entity, database, alertScheduleProvider),
                SensorType.Enum => new EnumSensorModel(entity, database, alertScheduleProvider),
                _ => throw new ArgumentException($"Unexpected sensor entity type {entity.Type}"),
            };
        }
    }
}