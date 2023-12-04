using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;
using System.Linq;

namespace HSMDataCollector.Converters
{
    internal static class ApiConverters
    {
        internal static AddOrUpdateSensorRequest ToApi(this InstantSensorOptions options)
        {
            var info = options.ToBaseInfo();

            info.Alerts = options.Alerts?.Select(u => u.ToApi()).ToList();

            return info;
        }


        internal static AddOrUpdateSensorRequest ToApi(this BarSensorOptions options)
        {
            var info = options.ToBaseInfo();

            info.Alerts = options.Alerts?.Select(u => u.ToApi()).ToList();

            return info;
        }


        private static AddOrUpdateSensorRequest ToBaseInfo(this SensorOptions options) =>
            new AddOrUpdateSensorRequest
            {
                TtlAlert = options.TtlAlert?.ToApi(),

                SensorType = options.Type,
                Path = options.Path,

                OriginalUnit = options.SensorUnit,
                Description = options.Description,

                TTL = options.TtlAlert?.TtlValue?.Ticks ?? options.TTL?.Ticks,
                KeepHistory = options.KeepHistory?.Ticks,
                SelfDestroy = options.SelfDestroy?.Ticks,

                EnableGrafana = options.EnableForGrafana,

                IsSingletonSensor = options.IsSingletonSensor | options.IsComputerSensor,
                AggregateData = options.AggregateData,
                Options = options.Options,

                DefaultAlertsOptions = options.DefaultAlertsOptions,
                IsForceUpdate = options.IsForceUpdate,
            };


        internal static AlertUpdateRequest ToApi(this AlertBaseTemplate alert) =>
            new AlertUpdateRequest()
            {
                Conditions = alert.Conditions?.Select(u => u.ToApi()).ToList(),

                ConfirmationPeriod = alert.ConfirmationPeriod?.Ticks,
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
