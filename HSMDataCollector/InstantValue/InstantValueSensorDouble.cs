using System;
using System.Text;
using System.Text.Json;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorDouble : InstantValueTypedSensorBase<double>
    {
        public InstantValueSensorDouble(string path, string productKey, string address) : base(path, productKey, $"{address}/double")
        {
        }

        public override void AddValue(object value)
        {
            double doubleValue = (double)value;
            lock (_syncRoot)
            {
                Value = doubleValue;
            }

            DoubleSensorValue data = GetDataObject();
            SendData(data);
        }

        private DoubleSensorValue GetDataObject()
        {
            DoubleSensorValue result = new DoubleSensorValue();
            lock (_syncRoot)
            {
                result.DoubleValue = Value;
            }

            result.Path = Path;
            result.Key = ProductKey;
            result.Time = DateTime.Now;
            return result;
        }

        protected override byte[] GetBytesData(object data)
        {
            DoubleSensorValue typedData = (DoubleSensorValue)data;
            string convertedString = JsonSerializer.Serialize(typedData);
            return Encoding.UTF8.GetBytes(convertedString);
        }
    }
}
