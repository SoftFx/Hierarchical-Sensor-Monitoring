using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using Newtonsoft.Json;
using System;

namespace HSMDataCollector.InstantValue
{
    [Obsolete("Use InstantValueSensor class")]
    class InstantValueSensorDouble : InstantValueTypedSensorBase<double>, IDoubleSensor
    {
        public InstantValueSensorDouble(string path, string productKey, IValuesQueue queue)
            : base(path, productKey, queue)
        {
        }

        public void AddValue(double value)
        {
            DoubleSensorValue data = new DoubleSensorValue() {DoubleValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey};
            SendValue(data);
        }

        public void AddValue(double value, string comment)
        {
            DoubleSensorValue data = new DoubleSensorValue() { DoubleValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey, Comment = comment};
            SendValue(data);
        }

        public void AddValue(double value, SensorStatus status, string comment = null)
        {
            DoubleSensorValue data = new DoubleSensorValue() { DoubleValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey, Status = status};
            if (!string.IsNullOrEmpty(comment))
            {
                data.Comment = comment;
            }
            SendValue(data);
        }

        private void SendValue(DoubleSensorValue data)
        {
            string serializedValue = GetStringData(data);
            CommonSensorValue commonValue = new CommonSensorValue();
            commonValue.TypedValue = serializedValue;
            commonValue.SensorType = SensorType.DoubleSensor;
            EnqueueData(commonValue);
        }

        public override SimpleSensorValue GetLastValueNew()
        {
            throw new NotImplementedException();
        }

        protected override string GetStringData(SensorValueBase data)
        {
            try
            {
                DoubleSensorValue typedData = (DoubleSensorValue)data;
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
