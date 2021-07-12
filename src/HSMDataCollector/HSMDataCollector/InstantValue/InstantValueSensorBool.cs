using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using Newtonsoft.Json;
using System;

namespace HSMDataCollector.InstantValue
{
    [Obsolete("Use InstantValueSensor class")]
    class InstantValueSensorBool : InstantValueTypedSensorBase<bool>, IBoolSensor
    {
        public InstantValueSensorBool(string path, string productKey, IValuesQueue queue) 
            : base(path, productKey, queue)
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

        public void AddValue(bool value, SensorStatus status, string comment = null)
        {
            BoolSensorValue data = new BoolSensorValue() { BoolValue = value, Path = Path, Status = status, Key = ProductKey, Time = DateTime.Now }; 
            if (!string.IsNullOrEmpty(comment))
            {
                data.Comment = comment;
            }
            SendValue(data);
        }

        private void SendValue(BoolSensorValue data)
        {
            string serializedValue = GetStringData(data);
            CommonSensorValue commonValue = new CommonSensorValue();
            commonValue.TypedValue = serializedValue;
            commonValue.SensorType = SensorType.BooleanSensor;
            EnqueueData(commonValue);
        }

        public override UnitedSensorValue GetLastValueNew()
        {
            throw new NotImplementedException();
        }

        protected override string GetStringData(SensorValueBase data)
        {
            try
            {
                BoolSensorValue typedData = (BoolSensorValue)data;
                return JsonConvert.SerializeObject(typedData);
                //return Serializer.Serialize(typedData);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return string.Empty;
            }
        }
    }
}
