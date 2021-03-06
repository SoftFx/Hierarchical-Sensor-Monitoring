﻿using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using Newtonsoft.Json;
using System;

namespace HSMDataCollector.InstantValue
{
    [Obsolete("Use InstantValueSensor class")]
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

        public void AddValue(int value, SensorStatus status, string comment = null)
        {
            IntSensorValue data = new IntSensorValue() { IntValue = value, Path = Path, Time = DateTime.Now, Key = ProductKey, Status = status};
            if (!string.IsNullOrEmpty(comment))
            {
                data.Comment = comment;
            }
            SendValue(data);
        }

        private void SendValue(IntSensorValue data)
        {
            string serializedValue = GetStringData(data);
            CommonSensorValue commonValue = new CommonSensorValue();
            commonValue.TypedValue = serializedValue;
            commonValue.SensorType = SensorType.IntSensor;
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
                IntSensorValue typedData = (IntSensorValue)data;
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
