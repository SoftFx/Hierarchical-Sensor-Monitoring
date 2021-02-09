using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorDouble : InstantValueTypedSensorBase<double>, IDoubleSensor
    {
        public InstantValueSensorDouble(string path, string productKey, string address) : base(path, productKey, $"{address}/double")
        {
        }

        public void AddValue(double value)
        {
            lock (_syncRoot)
            {
                Value = value;
            }

            DoubleSensorValue data = GetDataObject();
            //SendData(data);
            Task.Run(() => SendData(data));
            //SendData(data);
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
