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
        #region Deserialize

        BoolSensorValue GetBoolSensorValue(string json);
        IntSensorValue GetIntSensorValue(string json);
        DoubleSensorValue GetDoubleSensorValue(string json);
        StringSensorValue GetStringSensorValue(string json);
        IntBarSensorValue GetIntBarSensorValue(string json);
        DoubleBarSensorValue GetDoubleBarSensorValue(string json);

        #endregion

        #region Convert to history items

        SensorHistoryData Convert(ExtendedBarSensorData data);

        #endregion

        SensorData ConvertUnitedValue(UnitedSensorValue value, string productName,
            DateTime timeCollected, TransactionType transactionType);
        //SensorDataObject ConvertUnitedValueToDatabase(UnitedSensorValue value, DateTime timeCollected);
        SensorDataEntity ConvertUnitedValueToDatabase(UnitedSensorValue value, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown);
        BarSensorValueBase GetBarSensorValue(UnitedSensorValue value);
        SensorInfo Convert(string productName, SensorValueBase sensorValue);
    }
}