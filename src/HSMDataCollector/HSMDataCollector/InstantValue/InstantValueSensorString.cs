using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using Newtonsoft.Json;
using System;

namespace HSMDataCollector.InstantValue
{
    [Obsolete("Use InstantValueSensor class")]
    class InstantValueSensorString : InstantValueTypedSensorBase<string>, IStringSensor
    {
        private string _value;
        public InstantValueSensorString(string path, string productKey, IValuesQueue queue) 
            : base(path, productKey, queue)
        {
        }

        public void AddValue(string value)
        {
            StringSensorValue data = new StringSensorValue(){StringValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey};
            SendValue(data);
        }

        public void AddValue(string value, string comment)
        {
            StringSensorValue data = new StringSensorValue() { StringValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey, Comment = comment};
            SendValue(data);
        }

        public void AddValue(string value, SensorStatus status, string comment = null)
        {
            StringSensorValue data = new StringSensorValue() { StringValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey, Status = status};
            if (!string.IsNullOrEmpty(comment))
            {
                data.Comment = comment;
            }
            SendValue(data);
        }

        private void SendValue(StringSensorValue data)
        {
            string serializedValue = GetStringData(data);
            CommonSensorValue commonValue = new CommonSensorValue();
            commonValue.TypedValue = serializedValue;
            commonValue.SensorType = SensorType.StringSensor;
            EnqueueData(commonValue);
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

        public override SimpleSensorValue GetLastValueNew()
        {
            throw new NotImplementedException();
        }

        protected override string GetStringData(SensorValueBase data)
        {
            try
            {
                StringSensorValue typedData = (StringSensorValue)data;
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
