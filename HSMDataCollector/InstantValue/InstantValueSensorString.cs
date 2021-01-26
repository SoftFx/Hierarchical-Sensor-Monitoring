using System;
using System.Text;
using System.Text.Json;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorString : InstantValueSensorBase
    {
        private string _value;
        public InstantValueSensorString(string name, string path, string productKey, string address) : base(name, path, productKey, address)
        {
        }

        public override void AddValue(object value)
        {
            string stringValue = (string)value;
            lock (_syncRoot)
            {
                _value = stringValue;
            }

            StringSensorValue data = GetDataObject();
            SendData(data);
        }
        private StringSensorValue GetDataObject()
        {
            StringSensorValue result = new StringSensorValue();
            lock (_syncRoot)
            {
                result.StringValue = _value;
            }

            result.Path = Path;
            result.Key = ProductKey;
            result.Time = DateTime.Now;
            return result;
        }
        protected override byte[] GetBytesData(object data)
        {
            StringSensorValue typedData = (StringSensorValue)data;
            string convertedString = JsonSerializer.Serialize(typedData);
            return Encoding.UTF8.GetBytes(convertedString);
        }
    }
}
