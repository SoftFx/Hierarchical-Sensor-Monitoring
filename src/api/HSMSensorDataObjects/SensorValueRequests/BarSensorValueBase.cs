﻿using System;
using System.Collections.Generic;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public abstract class BarSensorValueBase : SensorValueBase
    {
        public DateTime OpenTime { get; set; }

        public DateTime CloseTime { get; set; }

        public int Count { get; set; }
    }


    public abstract class BarSensorValueBase<T> : BarSensorValueBase
    {
        public T Min { get; set; }

        public T Max { get; set; }

        public T Mean { get; set; }

        public T LastValue { get; set; }

        public Dictionary<double, T> Percentiles { get; set; }


        public BarSensorValueBase()
        {
            Percentiles = new Dictionary<double, T>();
        }
    }
}
