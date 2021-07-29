using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;
using Logger = HSMDataCollector.Logging.Logger;

namespace HSMDataCollector.CustomFuncSensor
{
    internal class OneParamFuncSensor<T, U> : CustomFuncSensorBase, IParamsFuncSensor<U, T>
    {
        private readonly Func<List<U>, T> _funcToInvoke;
        private readonly List<U> _paramsList;
        private readonly object _lockObj;
        private readonly NLog.Logger _logger;
        public OneParamFuncSensor(string path, string productKey, IValuesQueue queue, string description, TimeSpan timerSpan, SensorType type,
            Func<List<U>, T> funcToInvoke, bool isLogging) : base(path, productKey, queue, description, timerSpan, type)
        {
            _funcToInvoke = funcToInvoke;
            _paramsList = new List<U>();
            _lockObj = new object();
            if (isLogging)
            {
                _logger = Logger.Create(nameof(OneParamFuncSensor<T, U>));
            }
        }

        public override UnitedSensorValue GetLastValueNew()
        {
            return GetValueInternal();
        }
        protected override UnitedSensorValue GetInvokeResult()
        {
            return GetValueInternal();
        }

        private UnitedSensorValue GetValueInternal()
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
                throw;
            }
        }
        private UnitedSensorValue CreateDataObject(T value)
        {
            UnitedSensorValue valueObject = new UnitedSensorValue();
            valueObject.Data = value.ToString();
            valueObject.Description = Description;
            valueObject.Path = Path;
            valueObject.Key = ProductKey;
            valueObject.Time = DateTime.Now;
            valueObject.Type = Type;
            return valueObject;
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
            return TimerSpan;
        }

        public void RestartTimer(TimeSpan timeSpan)
        {
            RestartTimerInternal(timeSpan);
        }
    }
}
