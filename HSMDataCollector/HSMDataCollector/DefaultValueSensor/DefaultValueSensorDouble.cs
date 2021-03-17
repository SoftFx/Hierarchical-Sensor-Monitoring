using System;
using System.Text.Json;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;

namespace HSMDataCollector.DefaultValueSensor
{
    internal class DefaultValueSensorDouble : DefaultValueSensorBase<double>, IDefaultValueSensorDouble
    {
        public DefaultValueSensorDouble(string path, string productKey, IValuesQueue queue, double defaultValue) : base(path, productKey, queue, defaultValue)
        {
        }

        public override CommonSensorValue GetLastValue()
        {
            CommonSensorValue commonSensorValue = new CommonSensorValue();
            commonSensorValue.SensorType = SensorType.IntSensor;
            commonSensorValue.TypedValue = JsonSerializer.Serialize(GetValue());
            return commonSensorValue;
        }

        public void AddValue(double value)
        {
            throw new NotImplementedException();
        }

        private DoubleSensorData GetValue()
        {
            double val;
            lock (_syncRoot)
            {
                val = _currentValue;
            }

            DoubleSensorData result = new DoubleSensorData { DoubleValue = val };
            return result;
        }
    }
}
