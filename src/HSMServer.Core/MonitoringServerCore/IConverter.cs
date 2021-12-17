using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.MonitoringServerCore
{
    public interface IConverter
    {
        SensorData ConvertUnitedValue(UnitedSensorValue value, string productName,
            DateTime timeCollected, TransactionType transactionType);
        SensorDataEntity ConvertUnitedValueToDatabase(UnitedSensorValue value, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown);
        BarSensorValueBase GetBarSensorValue(UnitedSensorValue value);
    }
}