using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public sealed class AlertConditionTemplate
    {
        public AlertTargetTemplate Target { get; internal set; }

        public AlertCombination Combination { get; internal set; }

        public AlertOperation Operation { get; internal set; }

        public AlertProperty Property { get; internal set; }


        public static AlertConditionTemplate Build(AlertTargetTemplate target, AlertCombination combination, AlertOperation operation, AlertProperty property) =>
            new AlertConditionTemplate()
            {
                Target = target,
                Combination = combination,
                Operation = operation,
                Property = property
            };
    }


    public sealed class AlertTargetTemplate
    {
        public TargetType Type { get; internal set; }

        public string Value { get; internal set; }


        public static AlertTargetTemplate Build(TargetType type, string value) =>
            new AlertTargetTemplate()
            {
                Type = type,
                Value = value
            };
    }
}