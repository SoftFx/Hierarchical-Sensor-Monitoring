using System;
using System.Text.Json;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
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
            lock (_syncRoot)
            {
                _currentValue = value;
            }
        }

        private DoubleSensorValue GetValue()
        {
            double val;
            lock (_syncRoot)
            {
                val = _currentValue;
            }

            DoubleSensorValue result = new DoubleSensorValue() { DoubleValue = val, Key = ProductKey, Path = Path, Time = DateTime.Now };
            return result;
        }
    }
}
