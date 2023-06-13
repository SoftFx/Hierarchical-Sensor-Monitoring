using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.CustomFuncSensor
{
    internal sealed class NoParamsFuncSensor<T> : CustomFuncSensorBase, INoParamsFuncSensor<T>
    {
        private readonly ICollectorLogger _logger;
        private readonly Func<T> _funcToInvoke;


        public NoParamsFuncSensor(string path, IValuesQueue queue, string description, TimeSpan timerSpan, Func<T> funcToInvoke, ICollectorLogger logger)
            : base(path, queue, description, timerSpan)
        {
            _funcToInvoke = funcToInvoke;
            _logger = logger;
        }


        public Func<T> GetFunc()
        {
            return _funcToInvoke;
        }

        public TimeSpan GetInterval()
        {
            return _timerSpan;
        }

        public void RestartTimer(TimeSpan timeSpan)
        {
            RestartTimerInternal(timeSpan);
        }

        public override SensorValueBase GetLastValue()
        {
            return GetValueInternal();
        }

        protected override SensorValueBase GetInvokeResult()
        {
            return GetValueInternal();
        }

        private SensorValueBase GetValueInternal()
        {
            try
            {
                T value = _funcToInvoke.Invoke();
                return CreateDataObject(value);
            }
            catch (Exception e)
            {
                _logger?.Error(e);
                return CreateErrorDataObject(default(T), e);
            }
        }
    }
}
