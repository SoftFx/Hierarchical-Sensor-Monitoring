using System;
using System.Linq;
using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.Converters
{
    internal static class ApiConverters
    {

        internal static AddOrUpdateSensorRequest ToApi<TDisplayUnit>(this BaseInstantSensorOptions<TDisplayUnit> options) where TDisplayUnit : struct, Enum
        {
            var info = options.ToBaseInfo();

            info.Alerts = options.Alerts?.Select(u => u.ToApi()).ToList();

            info.DisplayUnit = Convert.ToInt32(options.DisplayUnit);

            return info;
        }

        internal static AddOrUpdateSensorRequest ToApi(this InstantSensorOptions options)
        {
            var info = options.ToBaseInfo();

            info.Alerts = options.Alerts?.Select(u => u.ToApi()).ToList();

            info.DisplayUnit = null;

            return info;
        }

        internal static AddOrUpdateSensorRequest ToApi(this EnumSensorOptions options)
        {
            var info = options.ToBaseInfo();

            info.Alerts = options.Alerts?.Select(u => u.ToApi()).ToList();

            info.DisplayUnit = null;

            info.EnumOptions = options.EnumOptions?.ToList();

            return info;
        }


        internal static AddOrUpdateSensorRequest ToApi(this BarSensorOptions options)
        {
            var info = options.ToBaseInfo();

            info.Alerts = options.Alerts?.Select(u => u.ToApi()).ToList();

            info.DisplayUnit = null;

            return info;
        }


        private static AddOrUpdateSensorRequest ToBaseInfo<TDisplayUnit>(this SensorOptions<TDisplayUnit> options) where TDisplayUnit : struct, Enum
        {
            return new AddOrUpdateSensorRequest
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
                Statistics = options.Statistics,

                DefaultAlertsOptions = options.DefaultAlertsOptions,
                IsForceUpdate = options.IsForceUpdate,

                DisplayUnit = options.DisplayUnit.HasValue ? Convert.ToInt32(options.DisplayUnit.Value) : (int?)null
            };
        }


        internal static AlertUpdateRequest ToApi(this AlertBaseTemplate alert) =>
            new AlertUpdateRequest()
            {
                Conditions = alert.Conditions?.Select(u => u.ToApi()).ToList(),

                ConfirmationPeriod = alert.ConfirmationPeriod?.Ticks,

                ScheduledNotificationTime = alert.ScheduledNotificationTime,
                ScheduledRepeatMode = alert.ScheduledRepeatMode,
                ScheduledInstantSend = alert.ScheduledInstantSend,

                DestinationMode = alert.DestinationMode,
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
