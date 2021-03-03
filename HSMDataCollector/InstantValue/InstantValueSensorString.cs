using System;
using System.Text;
using System.Text.Json;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorString : InstantValueSensorBase, IStringSensor
    {
        private string _value;
        public InstantValueSensorString(string path, string productKey, string address, IValuesQueue queue) 
            : base(path, productKey, $"{address}/string", queue)
        {
        }

        public void AddValue(string value)
        {
            StringSensorValue data = new StringSensorValue(){StringValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey};
            string serializedValue = GetStringData(data);
            CommonSensorValue commonValue = new CommonSensorValue();
            commonValue.TypedValue = serializedValue;
            commonValue.SensorType = SensorType.StringSensor;
            SendData(commonValue);
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
