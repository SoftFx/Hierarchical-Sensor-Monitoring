using System;
using System.Collections.Generic;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public abstract class BarSensorValueBase : SensorValueBase
    {
        public DateTime OpenTime { get; set; }

        public DateTime CloseTime { get; set; }

        public int Count { get; set; }
    }


    public abstract class BarSensorValueBase<T> : BarSensorValueBase where T : struct
    {
        public T Min { get; set; }

        public T Max { get; set; }

        public T Mean { get; set; }

        /// <summary>
        /// Population standard deviation of the bar's samples (sqrt(sum((x - mean)^2) / Count)),
        /// reported as a double for int bars too; 0 when Count is 1. <c>null</c> means unknown:
        /// an older collector that does not send the field, or a bar fed with pre-aggregated
        /// partials (AddPartial carries no spread). Unknown is never the same as 0.
        /// </summary>
        public double? StdDev { get; set; }

        public T? FirstValue { get; set; }

        public T LastValue { get; set; }

        [Obsolete("The property is not used in server calculations")]
        public Dictionary<double, T> Percentiles { get; set; }
    }
}
