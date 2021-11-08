using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using System;

namespace HSMServer.Core.SensorsDataProcessor
{
    public interface ISensorsProcessor
    {
        ValidationResult ProcessData(BoolSensorValue value, DateTime timeCollected, out SensorData processedData, out string processingError);
        ValidationResult ProcessData(IntSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError);
        ValidationResult ProcessData(DoubleSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError);
        ValidationResult ProcessData(StringSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError);
        ValidationResult ProcessData(DoubleBarSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError);
        ValidationResult ProcessData(IntBarSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError);
        ValidationResult ProcessData(FileSensorValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError);
        ValidationResult ProcessData(FileSensorBytesValue value, DateTime timeCollected, out SensorData processedData,
            out string processingError);
        ValidationResult ProcessUnitedData(UnitedSensorValue unitedValue, DateTime timeCollected,
            out SensorData processedData, out string processingError);
    }
}