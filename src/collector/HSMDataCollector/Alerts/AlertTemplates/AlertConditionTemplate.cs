using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public sealed class AlertConditionTemplate
    {
        public AlertTargetTemplate Target { get; internal set; }

        public AlertCombination Combination { get; internal set; }

        public AlertOperation Operation { get; internal set; }

        public AlertProperty Property { get; internal set; }

        static public AlertConditionTemplate Build(AlertTargetTemplate target, AlertCombination combination, AlertOperation operation, AlertProperty property)
        {
            return new AlertConditionTemplate()
            {
                Target = target,
                Combination = combination,
                Operation = operation,
                Property = property
            };
        }
    }

    public sealed class AlertTargetTemplate
    {
        public TargetType Type { get; internal set; }

        public string Value { get; internal set; }
        static public AlertTargetTemplate Build(TargetType type, string value)
        {
            return new AlertTargetTemplate()
            {
                Type = type,
                Value = value
            };
        }
    }
}