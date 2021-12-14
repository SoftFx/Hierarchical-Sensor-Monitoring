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
        FileSensorValue GetFileSensorValue(string json);

        #endregion

        #region Convert to history items

        SensorHistoryData Convert(ExtendedBarSensorData data);
        //SensorHistoryData Convert(SensorDataObject dataObject);
        SensorHistoryData Convert(SensorDataEntity dataObject);

        #endregion

        #region Independent update messages
        //SensorData Convert(SensorDataObject dataObject, SensorInfo sensorInfo, string productName);
        //SensorData Convert(SensorDataObject dataObject, string productName);
        SensorData Convert(SensorDataEntity dataObject, SensorInfo sensorInfo, string productName);
        SensorData Convert(SensorDataEntity dataObject, string productName);
        #endregion

        SensorData ConvertUnitedValue(UnitedSensorValue value, string productName,
            DateTime timeCollected, TransactionType transactionType);
        //SensorDataObject ConvertUnitedValueToDatabase(UnitedSensorValue value, DateTime timeCollected);
        SensorDataEntity ConvertUnitedValueToDatabase(UnitedSensorValue value, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown);
        BarSensorValueBase GetBarSensorValue(UnitedSensorValue value);
        SensorInfo Convert(string productName, string path);
        SensorInfo Convert(string productName, SensorValueBase sensorValue);
    }
}