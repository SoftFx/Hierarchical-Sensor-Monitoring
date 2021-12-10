﻿using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;

namespace HSMDataCollector.CustomFuncSensor
{
    internal class NoParamsFuncSensor<T> : CustomFuncSensorBase, INoParamsFuncSensor<T>
    {
        private readonly Func<T> _funcToInvoke;
        private readonly NLog.Logger _logger;
        public NoParamsFuncSensor(string path, string productKey, IValuesQueue queue, string description, TimeSpan timerSpan, SensorType type, Func<T> funcToInvoke,
            bool isLogging) : base( path, productKey, queue, description, timerSpan, type)
        {
            _funcToInvoke = funcToInvoke;
            if (isLogging)
            {
                _logger = Logger.Create(nameof(NoParamsFuncSensor<T>));
            }
        }
        
        public override UnitedSensorValue GetLastValue()
        {
            return GetValueInternal();
        }
        protected override UnitedSensorValue GetInvokeResult()
        {
            return GetValueInternal();
        }

        private UnitedSensorValue GetValueInternal()
        {
            try
            {
                T value = _funcToInvoke.Invoke();
                return CreateDataObject(value);
            }
            catch (Exception e)
            {
                _logger?.Error(e);
                return CreateErrorDataObject(e);
            }
        }

        public Func<T> GetFunc()
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
