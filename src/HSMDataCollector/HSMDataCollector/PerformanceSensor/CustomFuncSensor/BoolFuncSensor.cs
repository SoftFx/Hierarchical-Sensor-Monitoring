using HSMDataCollector.Core;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using Newtonsoft.Json;
using System;

namespace HSMDataCollector.PerformanceSensor.CustomFuncSensor
{
    internal class BoolFuncSensor : FuncSensorBase<bool>
    {
        public BoolFuncSensor(Func<bool> function, string path, string productKey, IValuesQueue queue, int timeout = 15000) : base(function, path, productKey, queue, timeout)
        {
        }

        public override SensorValueBase GetLastValueNew()
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
