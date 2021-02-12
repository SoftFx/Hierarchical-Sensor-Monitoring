using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorString : InstantValueSensorBase
    {
        private string _value;
        public InstantValueSensorString(string path, string productKey, string address, HttpClient client) 
            : base(path, productKey, $"{address}/string", client)
        {
        }

        public void AddValue(string value)
        {
            StringSensorValue data = new StringSensorValue(){StringValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey};
            //SendData(data);
            //Task.Run(() => SendData(data));
            string serializedValue = GetStringData(data);
            SendData(serializedValue);
        }
        private StringSensorValue GetDataObject()
        {
            StringSensorValue result = new StringSensorValue();
            lock (_syncObject)
            {
                result.StringValue = _value;
            }

            result.Path = Path;
            result.Key = ProductKey;
            result.Time = DateTime.Now;
            return result;
        }

        protected override string GetStringData(SensorValueBase data)
        {
            try
            {
                StringSensorValue typedData = (StringSensorValue)data;
                return JsonSerializer.Serialize(typedData);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return string.Empty;
            }
        }

        protected override byte[] GetBytesData(SensorValueBase data)
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
