using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.MonitoringServerCore
{
    public interface IConverter
    {
        SensorDataEntity ConvertUnitedValueToDatabase(UnitedSensorValue value, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown);
        BarSensorValueBase GetBarSensorValue(UnitedSensorValue value);
    }
}