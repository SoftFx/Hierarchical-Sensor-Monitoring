using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorBool : InstantValueTypedSensorBase<bool>, IBoolSensor
    {
        public InstantValueSensorBool(string path, string productKey, string address) : base(path, productKey, $"{address}/bool")
        {
        }

        public void AddValue(bool value)
        {
            lock (_syncRoot)
            {
                Value = value;
            }

            BoolSensorValue data = GetDataObject();
            //Task.Run(() => SendData(data));
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
