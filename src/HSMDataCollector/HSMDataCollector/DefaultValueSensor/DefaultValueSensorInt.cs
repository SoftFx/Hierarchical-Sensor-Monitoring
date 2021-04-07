using System;
//using System.Text.Json;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using Newtonsoft.Json;

namespace HSMDataCollector.DefaultValueSensor
{
    internal class DefaultValueSensorInt : DefaultValueSensorBase<int>, IDefaultValueSensorInt
    {
        public DefaultValueSensorInt(string path, string productKey, IValuesQueue queue, int defaultValue) : base(path, productKey, queue, defaultValue)
        {
        }

        public override CommonSensorValue GetLastValue()
        {
            CommonSensorValue commonSensorValue = new CommonSensorValue();
            commonSensorValue.SensorType = SensorType.IntSensor;
            commonSensorValue.TypedValue = JsonConvert.SerializeObject(GetValue());
            return commonSensorValue;
        }

        public void AddValue(int value)
        {
            lock (_syncRoot)
            {
                _currentValue = value;
            }
        }

        public void AddValue(int value, string comment)
        {
            lock (_syncRoot)
            {
                _currentValue = value;
                _currentComment = comment;
            }
        }

        public void AddValue(int value, SensorStatus status, string comment = null)
        {
            lock (_syncRoot)
            {
                _currentValue = value;
                _currentStatus = status;
                _currentComment = comment;
            }
        }

        private IntSensorValue GetValue()
        {
            int val;
            string comment;
            SensorStatus status;
            lock (_syncRoot)
            {
                val = _currentValue;
                comment = _currentComment;
                status = _currentStatus;
            }

            IntSensorValue result = new IntSensorValue() { IntValue = val, Path = Path, Key = ProductKey, Time = DateTime.Now, Comment = comment, Status = status };
            return result;
        }
    }
}
