using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public sealed class AlertConditionTemplate
    {
        public AlertTargetTemplate Target { get; internal set; }

        public AlertCombination Combination { get; internal set; }

        public AlertOperation Operation { get; internal set; }

        public AlertProperty Property { get; internal set; }
    }


    public sealed class AlertTargetTemplate
    {
        public TargetType Type { get; internal set; }

        public string Value { get; internal set; }
    }
}