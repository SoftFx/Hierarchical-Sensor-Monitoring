using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using System;
using HSMSensorDataObjects;

namespace HSMServer.Core.MonitoringServerCore
{
    public interface IConverter
    {
        #region Convert to history items

        SensorHistoryData Convert(ExtendedBarSensorData data);

        #endregion

        SensorData ConvertUnitedValue(UnitedSensorValue value, string productName,
            DateTime timeCollected, TransactionType transactionType);
        SensorDataEntity ConvertUnitedValueToDatabase(UnitedSensorValue value, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown);
        BarSensorValueBase GetBarSensorValue(UnitedSensorValue value);
    }
}