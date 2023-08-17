using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public sealed class AlertConditionBuildRequest
    {
        public AlertTargetBuildRequest Target { get; internal set; }

        public AlertCombination Combination { get; internal set; }

        public AlertOperation Operation { get; internal set; }

        public AlertProperty Property { get; internal set; }
    }


    public sealed class AlertTargetBuildRequest
    {
        public TargetType Type { get; internal set; }

        public string Value { get; internal set; }
    }
}