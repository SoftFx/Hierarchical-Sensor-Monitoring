using System.ComponentModel;

namespace HSMSensorDataObjects.SensorValueRequests
{
    public class DoubleBarSensorValue : BarSensorValueBase<double>
    {
        [DefaultValue((int)SensorType.DoubleBarSensor)]
        public override SensorType Type => SensorType.DoubleBarSensor;

        /// <summary>
        /// Population standard deviation of the bar's samples (sqrt(sum((x - mean)^2) / Count)),
        /// a double on int bars too; 0 when Count is 1 (#1509). Sent by the native collector; the
        /// managed HSMDataCollector does not compute it (owner decision 2026-10-07, #1529) — its bar
        /// types derive from BarSensorValueBase&lt;T&gt;, not from this class, so its wire is unchanged.
        /// <c>null</c> or absent means unknown (an older or managed collector, or a bar fed with
        /// pre-aggregated partials); unknown is never the same as 0.
        /// </summary>
        public double? StdDev { get; set; }
    }
}
