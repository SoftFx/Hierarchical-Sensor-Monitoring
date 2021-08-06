using System;
using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.SensorsDatabase
{
    internal interface ISensorsDatabase
    {
        long DatabaseMinTicks { get; }
        long DatabaseMaxTicks { get; }
        List<SensorDataEntity> GetAllSensorValues();
        List<SensorDataEntity> GetSensorValues(int count);
        List<SensorDataEntity> GetSensorValuesFrom(DateTime from);
        List<SensorDataEntity> GetSensorValuesBetween(DateTime from, DateTime to);
    }
}