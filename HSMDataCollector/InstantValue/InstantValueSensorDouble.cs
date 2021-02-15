using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorDouble : InstantValueTypedSensorBase<double>, IDoubleSensor
    {
        public InstantValueSensorDouble(string path, string productKey, string address, HttpClient client)
            : base(path, productKey, $"{address}/double", client)
        {
        }

        public void AddValue(double value)
        {
            DoubleSensorValue data = new DoubleSensorValue() {DoubleValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey};
            string serializedValue = GetStringData(data);
            Task.Run(() => SendData(serializedValue));
            //SendData(data);
        }

        private DoubleSensorValue GetDataObject()
        {
            DoubleSensorValue result = new DoubleSensorValue();
            lock (_syncObject)
            {
                result.DoubleValue = Value;
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
                DoubleSensorValue typedData = (DoubleSensorValue)data;
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
                DoubleSensorValue typedData = (DoubleSensorValue)data;
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
