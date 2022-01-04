using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.MonitoringServerCore
{
    public interface IConverter
    {
        BarSensorValueBase GetBarSensorValue(UnitedSensorValue value);
    }
}