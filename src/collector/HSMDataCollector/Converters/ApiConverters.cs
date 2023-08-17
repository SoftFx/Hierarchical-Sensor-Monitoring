using HSMDataCollector.Alerts;
using HSMDataCollector.SensorsMetainfo;
using HSMSensorDataObjects.SensorRequests;
using System.Linq;

namespace HSMDataCollector.Converters
{
    internal static class ApiConverters
    {
        internal static AddOrUpdateSensorRequest ToApi(this SensorMetainfo info) =>
            new AddOrUpdateSensorRequest()
            {
                Alerts = info.Alerts?.Select(u => u.ToApi()).ToList(),
                TtlAlert = info.TtlAlert?.ToApi(),

                Description = info.Description,
                SensorType = info.SensorType,
                Path = info.Path,

                OriginalUnit = info.OriginalUnit,

                KeepHistory = info.Settings.KeepHistory?.Ticks,
                SelfDestroy = info.Settings.SelfDestroy?.Ticks,
                TTL = info.Settings.TTL?.Ticks,

                AggregateData = info.AggregateData,

                EnableGrafana = info.Enables.ForGrafana,
            };


        internal static AlertUpdateRequest ToApi(this AlertBaseTemplate alert) =>
            new AlertUpdateRequest()
            {
                Conditions = alert.Conditions?.Select(u => u.ToApi()).ToList(),

                Template = alert.Template,
                Status = alert.Status,
                Icon = alert.Icon,

                IsDisabled = alert.IsDisabled,
            };


        internal static AlertConditionUpdate ToApi(this AlertConditionTemplate condition) =>
            new AlertConditionUpdate()
            {
                Combination = condition.Combination,
                Operation = condition.Operation,
                Property = condition.Property,

                Target = condition.Target.ToApi(),
            };


        internal static TargetValue ToApi(this AlertTargetTemplate target) =>
            new TargetValue()
            {
                Value = target.Value,
                Type = target.Type,
            };
    }
}
