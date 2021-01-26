using System;
using System.Text;
using System.Text.Json;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorBool : InstantValueTypedSensorBase<bool>
    {
        public InstantValueSensorBool(string name, string path, string productKey, string address) : base(name, path, productKey, address)
        {
        }

        public override void AddValue(object value)
        {
            bool boolValue = (bool) value;
            lock (_syncRoot)
            {
                Value = boolValue;
            }

            BoolSensorValue data = GetDataObject();
            SendData(data);
        }

        private BoolSensorValue GetDataObject()
        {
            BoolSensorValue result = new BoolSensorValue();
            lock (_syncRoot)
            {
                result.BoolValue = Value;
            }

            result.Path = Path;
            result.Key = ProductKey;
            result.Time = DateTime.Now;
            return result;
        }

        protected override byte[] GetBytesData(object data)
        {
            BoolSensorValue typedData = (BoolSensorValue) data;
            string convertedString = JsonSerializer.Serialize(typedData);
            return Encoding.UTF8.GetBytes(convertedString);
        }
    }
}
