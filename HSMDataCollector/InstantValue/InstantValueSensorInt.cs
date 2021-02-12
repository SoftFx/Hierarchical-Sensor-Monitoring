using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorInt : InstantValueTypedSensorBase<int>, IIntSensor
    {
        public InstantValueSensorInt(string path, string productKey, string address, HttpClient client) 
            : base(path, productKey, $"{address}/int", client)
        {
        }

        public void AddValue(int value)
        {
            IntSensorValue data = new IntSensorValue() {IntValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey};
            //SendData(data);
            //Task.Run(() => SendData(data));
            string serializedValue = GetStringData(data);
            SendData(serializedValue);
        }
        private IntSensorValue GetDataObject()
        {
            IntSensorValue result = new IntSensorValue();
            lock (_syncObject)
            {
                result.IntValue = Value;
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
                IntSensorValue typedData = (IntSensorValue)data;
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
                IntSensorValue typedData = (IntSensorValue)data;
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
