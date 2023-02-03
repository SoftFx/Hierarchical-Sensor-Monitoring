using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorValueRequests;
using NLog;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.CustomFuncSensor
{
    internal sealed class OneParamFuncSensor<T, U> : CustomFuncSensorBase, IParamsFuncSensor<T, U>
    {
        private readonly Func<List<U>, T> _funcToInvoke;
        private readonly List<U> _paramsList;
        private readonly object _lockObj;
        private readonly Logger _logger;


        public OneParamFuncSensor(string path, IValuesQueue queue, string description, TimeSpan timerSpan, Func<List<U>, T> funcToInvoke, Logger logger)
            : base(path, queue, description, timerSpan)
        {
            _funcToInvoke = funcToInvoke;
            _paramsList = new List<U>();
            _lockObj = new object();
            _logger = logger;
        }


        public void AddValue(U value)
        {
            lock (_lockObj)
            {
                _paramsList.Add(value);
            }
        }

        public Func<List<U>, T> GetFunc()
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
            List<U> listCopy;
            lock (_lockObj)
            {
                listCopy = new List<U>(_paramsList);
                _paramsList.Clear();
            }

            try
            {
                var value = _funcToInvoke.Invoke(listCopy);
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
