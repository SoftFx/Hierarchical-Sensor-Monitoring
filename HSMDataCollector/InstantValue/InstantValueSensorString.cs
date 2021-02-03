using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorString : InstantValueSensorBase
    {
        private string _value;
        public InstantValueSensorString(string path, string productKey, string address) : base(path, productKey, $"{address}/string")
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
            //SendData(data);
            Task.Run(() => SendData(data));
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
            try
            {
                StringSensorValue typedData = (StringSensorValue)data;
                string convertedString = JsonSerializer.Serialize(typedData);
                return Encoding.UTF8.GetBytes(convertedString);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return new byte[1];
            }
            
        }
    }
}
