using System;
using HSMCommon.Model;
using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.DataLayer.Model;
using HSMServer.Model;

namespace HSMServer.MonitoringServerCore
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
        SensorHistoryData Convert(SensorDataObject dataObject);

        #endregion

        #region Convert to database objects

        SensorDataObject ConvertToDatabase(BoolSensorValue sensorValue, DateTime timeCollected);
        SensorDataObject ConvertToDatabase(IntSensorValue sensorValue, DateTime timeCollected);
        SensorDataObject ConvertToDatabase(DoubleSensorValue sensorValue, DateTime timeCollected);
        SensorDataObject ConvertToDatabase(StringSensorValue sensorValue, DateTime timeCollected);
        SensorDataObject ConvertToDatabase(FileSensorValue sensorValue, DateTime timeCollected);
        SensorDataObject ConvertToDatabase(FileSensorBytesValue sensorValue, DateTime timeCollected);
        SensorDataObject ConvertToDatabase(IntBarSensorValue sensorValue, DateTime timeCollected);
        SensorDataObject ConvertToDatabase(DoubleBarSensorValue sensorValue, DateTime timeCollected);

        #endregion

        #region Independent update messages

        SensorData Convert(SensorDataObject dataObject, SensorInfo sensorInfo, string productName);
        SensorData Convert(SensorDataObject dataObject, string productName);
        SensorData Convert(BoolSensorValue value, string productName, DateTime timeCollected, TransactionType type);
        SensorData Convert(IntSensorValue value, string productName, DateTime timeCollected, TransactionType type);
        SensorData Convert(DoubleSensorValue value, string productName, DateTime timeCollected, TransactionType type);
        SensorData Convert(StringSensorValue value, string productName, DateTime timeCollected, TransactionType type);
        SensorData Convert(FileSensorValue value, string productName, DateTime timeCollected, TransactionType type);
        SensorData Convert(FileSensorBytesValue value, string productName, DateTime timeCollected,
            TransactionType type);
        SensorData Convert(IntBarSensorValue value, string productName, DateTime timeCollected, TransactionType type);
        SensorData Convert(DoubleBarSensorValue value, string productName, DateTime timeCollected,
            TransactionType type);

        #endregion

        #region UnitedSensorValue to update messages

        //SensorData ConvertBool(UnitedSensorValue value, string productName, DateTime timeCollected);
        //SensorData ConvertInt(UnitedSensorValue value, string productName, DateTime timeCollected);
        //SensorData ConvertDouble(UnitedSensorValue value, string productName, DateTime timeCollected);
        //SensorData ConvertString(UnitedSensorValue value, string productName, DateTime timeCollected);
        //SensorData ConvertIntBar(UnitedSensorValue value, string productName, DateTime timeCollected);
        //SensorData ConvertDoubleBar(UnitedSensorValue value, string productName, DateTime timeCollected);
        SensorData ConvertUnitedValue(UnitedSensorValue value, string productName, DateTime timeCollected);

        #endregion

        #region UnitedSensorValue to database objects

        //SensorDataObject ConvertBoolToDatabase(UnitedSensorValue value, DateTime timeCollected);
        //SensorDataObject ConvertIntToDatabase(UnitedSensorValue value, DateTime timeCollected);
        //SensorDataObject ConvertDoubleToDatabase(UnitedSensorValue value, DateTime timeCollected);
        //SensorDataObject ConvertStringToDatabase(UnitedSensorValue value, DateTime timeCollected);
        //SensorDataObject ConvertIntBarToDatabase(UnitedSensorValue value, DateTime timeCollected);
        //SensorDataObject ConvertDoubleBarToDatabase(UnitedSensorValue value, DateTime timeCollected);
        SensorDataObject ConvertUnitedValueToDatabase(UnitedSensorValue value, DateTime timeCollected);
        #endregion

        SensorInfo Convert(string productName, string path);
        SensorInfo Convert(string productName, SensorValueBase sensorValue);

    }
}