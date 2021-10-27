using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMSensorDataObjects;
using System;
using System.Threading;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.CustomFuncSensor
{
    internal abstract class CustomFuncSensorBase : SensorBase
    {
        private Timer _internalTimer;
        protected readonly SensorType Type;
        protected TimeSpan TimerSpan;
        protected CustomFuncSensorBase(string path, string productKey, IValuesQueue queue, string description, TimeSpan timerSpan, SensorType type)
            : base(path, productKey, queue, description)
        {
            Type = type;
            RestartTimerInternal(timerSpan);
            TimerSpan = timerSpan;
        }
        private void TimerCallback(object state)
        {
            UnitedSensorValue value = GetInvokeResult();
            EnqueueValue(value);
        }
        protected UnitedSensorValue CreateErrorDataObject(Exception ex)
        {
            UnitedSensorValue result = new UnitedSensorValue();
            result.Key = ProductKey;
            result.Path = Path;
            result.Status = SensorStatus.Error;
            result.Time = DateTime.Now;
            result.Comment = $"Error occurred! {ex.ToString()}";
            result.Type = Type;
            result.Data = ex.Message;
            return result;
        }
        protected abstract UnitedSensorValue GetInvokeResult();

        protected void RestartTimerInternal(TimeSpan timerSpan)
        {
            if (_internalTimer != null)
            {
                _internalTimer.Change(timerSpan, timerSpan);
                return;
            }

            _internalTimer = new Timer(TimerCallback, null, timerSpan, timerSpan);
        }
        public override void Dispose()
        {
            _internalTimer?.Dispose();
        }

        public override bool HasLastValue => true;
    }
}
