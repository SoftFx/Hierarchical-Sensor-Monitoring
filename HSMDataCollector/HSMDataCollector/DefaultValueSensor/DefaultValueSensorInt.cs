using System.Text.Json;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;

namespace HSMDataCollector.DefaultValueSensor
{
    internal class DefaultValueSensorInt : DefaultValueSensorBase<int>, IDefaultValueSensorInt
    {
        public DefaultValueSensorInt(string path, string productKey, IValuesQueue queue, int defaultValue) : base(path, productKey, queue, defaultValue)
        {
        }

        public override CommonSensorValue GetLastValue()
        {
            CommonSensorValue commonSensorValue = new CommonSensorValue();
            commonSensorValue.SensorType = SensorType.IntSensor;
            commonSensorValue.TypedValue = JsonSerializer.Serialize(GetValue());
            return commonSensorValue;
        }

        public void AddValue(int value)
        {
            lock (_syncRoot)
            {
                _currentValue = value;
            }
        }

        private IntSensorData GetValue()
        {
            int val;
            lock (_syncRoot)
            {
                val = _currentValue;
            }

            IntSensorData result = new IntSensorData { IntValue = val };
            return result;
        }
    }
}
