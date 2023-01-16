using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Threading;

namespace HSMDataCollector.CustomFuncSensor
{
    internal abstract class CustomFuncSensorBase : SensorBase
    {
        private Timer _internalTimer;

        protected readonly SensorType _type;
        protected TimeSpan _timerSpan;

        public override bool HasLastValue => true;


        protected CustomFuncSensorBase(string path, IValuesQueue queue, string description, TimeSpan timerSpan, SensorType type)
            : base(path, queue, description)
        {
            _type = type;
            RestartTimerInternal(timerSpan);
            _timerSpan = timerSpan;
        }


        public override void Dispose()
        {
            _internalTimer?.Dispose();
        }

        protected abstract SensorValueBase GetInvokeResult();

        protected SensorValueBase CreateErrorDataObject<T>(T value, Exception ex) =>
            CreateDataObject(value, $"Error occurred! {ex}", SensorStatus.Error);

        protected SensorValueBase CreateDataObject<T>(T value, string comment = "", SensorStatus status = SensorStatus.Ok)
        {
            var valueObject = SensorValuesFactory.BuildValue(value);

            valueObject.Path = Path;
            valueObject.Time = DateTime.Now;
            valueObject.Status = status;
            valueObject.Comment = comment;

            return valueObject;
        }

        protected void RestartTimerInternal(TimeSpan timerSpan)
        {
            if (_internalTimer != null)
            {
                _internalTimer.Change(timerSpan, timerSpan);
                return;
            }

            _internalTimer = new Timer(TimerCallback, null, timerSpan, timerSpan);
        }

        private void TimerCallback(object state)
        {
            var value = GetInvokeResult();
            EnqueueValue(value);
        }
    }
}
