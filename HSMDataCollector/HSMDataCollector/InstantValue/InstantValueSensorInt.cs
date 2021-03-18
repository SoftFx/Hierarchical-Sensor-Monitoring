using System;
using System.Text;
using System.Text.Json;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Serialization;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensorInt : InstantValueTypedSensorBase<int>, IIntSensor
    {
        public InstantValueSensorInt(string path, string productKey, IValuesQueue queue) 
            : base(path, productKey, queue)
        {
        }

        public void AddValue(int value)
        {
            IntSensorValue data = new IntSensorValue() {IntValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey};
            SendValue(data);   
        }

        public void AddValue(int value, string comment)
        {
            IntSensorValue data = new IntSensorValue() { IntValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey, Comment = comment};
            SendValue(data);
        }

        private void SendValue(IntSensorValue data)
        {
            string serializedValue = GetStringData(data);
            CommonSensorValue commonValue = new CommonSensorValue();
            commonValue.TypedValue = serializedValue;
            commonValue.SensorType = SensorType.IntSensor;
            SendData(commonValue);
        }
        
        protected override string GetStringData(SensorValueBase data)
        {
            try
            {
                IntSensorValue typedData = (IntSensorValue)data;
                return JsonSerializer.Serialize(typedData);
                //return Serializer.Serialize(typedData);
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
                //string convertedString = Serializer.Serialize(typedData);
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
