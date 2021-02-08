using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HSMSensorDataObjects;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorInt : InstantValueTypedSensorBase<int>
    {
        public InstantValueSensorInt(string path, string productKey, string address) : base(path, productKey, $"{address}/int")
        {
        }

        public override void AddValue(object value)
        {
            int intValue = (int)value;
            lock (_syncRoot)
            {
                Value = intValue;
            }

            IntSensorValue data = GetDataObject();
            //SendData(data);
            //Task.Run(() => SendData(data));
            SendData(data);
        }
        private IntSensorValue GetDataObject()
        {
            IntSensorValue result = new IntSensorValue();
            lock (_syncRoot)
            {
                result.IntValue = Value;
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
