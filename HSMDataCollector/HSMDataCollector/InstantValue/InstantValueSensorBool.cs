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
    class InstantValueSensorBool : InstantValueTypedSensorBase<bool>, IBoolSensor
    {
        public InstantValueSensorBool(string path, string productKey, string address, IValuesQueue queue) 
            : base(path, productKey, $"{address}/bool", queue)
        {
        }

        public void AddValue(bool value)
        {
            BoolSensorValue data = new BoolSensorValue() {BoolValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey};
            SendValue(data);
        }

        public void AddValue(bool value, string comment)
        {
            BoolSensorValue data = new BoolSensorValue() {BoolValue = value, Comment = comment, Path = Path, Time = DateTime.Now, Key = ProductKey};
            SendValue(data);
        }

        private void SendValue(BoolSensorValue data)
        {
            string serializedValue = GetStringData(data);
            CommonSensorValue commonValue = new CommonSensorValue();
            commonValue.TypedValue = serializedValue;
            commonValue.SensorType = SensorType.BooleanSensor;
            SendData(commonValue);
        }

        protected override string GetStringData(SensorValueBase data)
        {
            try
            {
                BoolSensorValue typedData = (BoolSensorValue)data;
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
                BoolSensorValue typedData = (BoolSensorValue)data;
                //string convertedString = JsonSerializer.Serialize(typedData);
                string convertedString = Serializer.Serialize(typedData);
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
