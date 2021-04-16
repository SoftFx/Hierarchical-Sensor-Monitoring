using System;
using System.Threading;
using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMSensorDataObjects;

namespace HSMDataCollector.PerformanceSensor.CustomFuncSensor
{
    internal abstract class FuncSensorBase<T> : SensorBase
    {
        protected Func<T> Function;
        protected Timer _valuesTimer;
        internal FuncSensorBase(Func<T> function, string path, string productKey, IValuesQueue queue,int timeout = 150000)
            : base(path, productKey, queue)
        {
            Function = function;
            _valuesTimer = new Timer(OnTimerTick, null, timeout, timeout);
        }

        private void OnTimerTick(object state)
        {
            T value = default(T);
            try
            {
                value = Function.Invoke();
                CommonSensorValue convertedValue = ConvertValue(value);
                SendData(convertedValue);
            }
            catch (Exception e)
            { }
        }

        protected abstract CommonSensorValue ConvertValue(T value);
        public override void Dispose()
        {
            _valuesTimer.Dispose();
        }

        public override CommonSensorValue GetLastValue()
        {
            return null;
        }

        public override bool HasLastValue => false;
    }
}
