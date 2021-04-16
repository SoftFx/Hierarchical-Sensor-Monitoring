using System;
using HSMDataCollector.Core;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using Newtonsoft.Json;

namespace HSMDataCollector.PerformanceSensor.CustomFuncSensor
{
    internal class BoolFuncSensor : FuncSensorBase<bool>
    {
        public BoolFuncSensor(Func<bool> function, string path, string productKey, IValuesQueue queue, int timeout = 150000) : base(function, path, productKey, queue, timeout)
        {
        }

        protected override byte[] GetBytesData(SensorValueBase data)
        {
            throw new NotImplementedException();
        }

        protected override string GetStringData(SensorValueBase data)
        {
            throw new NotImplementedException();
        }

        protected override CommonSensorValue ConvertValue(bool value)
        {
            CommonSensorValue result = new CommonSensorValue();
            BoolSensorValue boolValue = new BoolSensorValue() {BoolValue = value, Path = Path, Key = ProductKey, Time = DateTime.Now};
            result.TypedValue = JsonConvert.SerializeObject(boolValue);
            result.SensorType = SensorType.BooleanSensor;
            return result;
        }
    }
}
