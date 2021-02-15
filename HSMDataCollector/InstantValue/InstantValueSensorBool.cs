using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorBool : InstantValueTypedSensorBase<bool>, IBoolSensor
    {
        public InstantValueSensorBool(string path, string productKey, string address, HttpClient client) 
            : base(path, productKey, $"{address}/bool", client)
        {
        }

        public void AddValue(bool value)
        {
            BoolSensorValue data = new BoolSensorValue() {BoolValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey};
            string serializedValue = GetStringData(data);
            Task.Run(() => SendData(serializedValue));
            //SendData(data);
        }

        private BoolSensorValue GetDataObject()
        {
            BoolSensorValue result = new BoolSensorValue();
            lock (_syncObject)
            {
                result.BoolValue = Value;
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
                BoolSensorValue typedData = (BoolSensorValue)data;
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
                BoolSensorValue typedData = (BoolSensorValue)data;
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
