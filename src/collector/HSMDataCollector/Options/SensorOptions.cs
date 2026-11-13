using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HSMDataCollector.Alerts;
using HSMDataCollector.Converters;
using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMDataCollector.Prototypes;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.Options
{
    public interface IMonitoringOptions
    {
        TimeSpan PostDataPeriod { get; set; }
    }

    public abstract class SensorOptions
    {

        internal SensorType Type { get; set; }

        internal string ComputerName { get; set; }

        internal string Module { get; set; }

        internal string Path { get; set; }

        internal DataProcessor DataProcessor { get; set; }


        public bool IsComputerSensor { get; set; } // singltone options sets dy default and sensor adds to .computer node

        public bool IsPrioritySensor { get; set; } // data sends in separate request

        public List<SpecialAlertTemplate> TtlAlerts { get; set; }

        // Convenience: single TTL alert (adds to TtlAlerts)
        public SpecialAlertTemplate TtlAlert { get => TtlAlerts?.FirstOrDefault(); set => TtlAlerts = value != null ? new List<SpecialAlertTemplate> { value } : null; }

        public string Description { get; set; }

        public Unit? SensorUnit { get; set; }


        [Obsolete("This setting doesn't exist for sensor now")]
        public DefaultChatsMode? DefaultChats { get; set; }

        public TimeSpan? KeepHistory { get; set; }

        public TimeSpan? SelfDestroy { get; set; }

        public List<TimeSpan?> TTLs { get; set; }

        // Convenience: single TTL interval
        public TimeSpan? TTL { get => TTLs?.FirstOrDefault(); set => TTLs = value != null ? new List<TimeSpan?> { value } : null; }

        public StatisticsOptions Statistics { get; set; }

        public bool? EnableForGrafana { get; set; }

        public bool? IsSingletonSensor { get; set; }

        public bool? AggregateData { get; set; }

        public DefaultAlertsOptions DefaultAlertsOptions { get; set; }

        public bool IsForceUpdate { get; set; } // if true then DataCollector can chage user settings

        public SensorLocation SensorLocation { get; set; }

        /// <summary>
        /// Paths this sensor was registered under before a layout change, each a FULL path
        /// relative to the product as the server tree shows it (e.g. <c>garage-server/System/CPU</c>)
        /// — no computer/module prefix is added. On registration a server that supports sensor
        /// move moves the sensor at such a path here, keeping its history, settings and alerts
        /// (merging when this path already has a sensor of the same type). Repeating it on every
        /// registration is safe; older servers ignore it. Null or blank entries are skipped.
        /// The list belongs to ONE sensor: a helper that builds several sensors from one options
        /// object (<c>AddSystemMonitoringSensors</c>, <c>AddAllDisksMonitoringSensors</c>, ...)
        /// refuses options that carry previous paths with an <see cref="ArgumentException"/> —
        /// set them on each sensor's own <c>Add*</c> call instead. The same holds for your own
        /// sensors: do not reuse an options object that carries previous paths across several
        /// <c>Create*Sensor</c> calls — the sensors would share the list, and the first of them to
        /// register would take the old sensor's history (a merge cannot be undone).
        /// </summary>
        public List<string> PreviousPaths { get; set; }

        // A previous path is a full path of one sensor: shared by several sensors it would move
        // that sensor's history into whichever of them registers first (#1724 review).
        internal static T ForSeveralSensors<T>(T options, string helper) where T : SensorOptions
        {
            if (ApiConverters.PreviousPathsOf(options) != null)
                throw new ArgumentException($"{helper} builds several sensors from one options object; " +
                    "PreviousPaths belongs to one sensor — set it on each sensor's own Add* call.", nameof(options));

            return options;
        }

        internal string CalculateSystemPath()
        {
            if (IsComputerSensor)
            {
                return DefaultPrototype.BuildPath(ComputerName, Path);
            }
            else
            {
                switch (SensorLocation)
                {
                    case SensorLocation.Module:  return DefaultPrototype.BuildPath(ComputerName, Module, Path);
                    case SensorLocation.Product: return DefaultPrototype.BuildPath(Path);
                    default: return DefaultPrototype.BuildPath(Path);
                }
            }
        }

        internal object Copy() => MemberwiseClone();

    }


    public abstract class SensorOptions<TDisplayUnit> : SensorOptions where TDisplayUnit : struct, Enum
    {
        internal abstract AddOrUpdateSensorRequest ApiRequest { get; }

        public TDisplayUnit? DisplayUnit { get; set; }
    }


    public class FileSensorOptions : InstantSensorOptions
    {
        /// <summary>
        /// Hard ceiling for <see cref="MaxFileSizeBytes"/>. The file is fully buffered in memory and
        /// JSON-serializes bytes as a numeric array (~4x expansion), so an unbounded user-configured
        /// cap is an OOM vector (#1102-C1). Configured values above this are clamped at send time.
        /// </summary>
        public const long MaxAllowedFileSizeBytes = 128L * 1024 * 1024;

        public string DefaultFileName { get; set; }

        public string Extension { get; set; }

        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    }

    public abstract class BaseInstantSensorOptions<TDisplayUnit> : SensorOptions<TDisplayUnit> where TDisplayUnit : struct, Enum
    {
        public List<InstantAlertTemplate> Alerts { get; set; }

        internal override AddOrUpdateSensorRequest ApiRequest => this.ToApi();
    }


    public class InstantSensorOptions : BaseInstantSensorOptions<NoDisplayUnit>
    {
    }


    public class EnumSensorOptions : InstantSensorOptions
    {
        internal override AddOrUpdateSensorRequest ApiRequest => this.ToApi();
        public List<EnumOption> EnumOptions { get; set; }

        public EnumSensorOptions()
        {
            AggregateData = true;
        }

        public string GenerateEnumOptionsDecription()
        {
            if (EnumOptions?.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(1024);
            foreach (var option in EnumOptions)
            {
               sb.AppendLine($"* ({option.Key}) {option.Value} - {option.Description}");
            }

            sb.AppendLine();;

            return sb.ToString();
        }
    }

    public abstract class BaseMonitoringInstantSensorOptions<TDisplayUnit> : BaseInstantSensorOptions<TDisplayUnit>, IMonitoringOptions where TDisplayUnit : struct, Enum
    {
        public virtual TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromSeconds(15);

    }


    public class MonitoringInstantSensorOptions : InstantSensorOptions, IMonitoringOptions
    {
        public virtual TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromSeconds(15);
    }


    public class RateSensorOptions : BaseMonitoringInstantSensorOptions<RateDisplayUnit>
    {
        public override TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromMinutes(1);

        public RateSensorOptions()
        {
            SensorUnit = Unit.ValueInSecond;
        }
    }


    public class FunctionSensorOptions : MonitoringInstantSensorOptions
    {
        public override TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromMinutes(1);
    }


    public class ValuesFunctionSensorOptions : MonitoringInstantSensorOptions
    {
        public override TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromMinutes(1);

        public int MaxCacheSize { get; set; } = 10000;
    }


    public class NetworkSensorOptions : MonitoringInstantSensorOptions
    {
        public NetworkSensorOptions()
        {
            PostDataPeriod = TimeSpan.FromMinutes(1);
        }
    }


    public class BarSensorOptions : SensorOptions<NoDisplayUnit>, IMonitoringOptions
    {
        public List<BarAlertTemplate> Alerts { get; set; }

        public TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromSeconds(15);

        public TimeSpan BarTickPeriod { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan BarPeriod { get; set; } = TimeSpan.FromMinutes(5);

        public int Precision { get; set; } = 2;


        internal override AddOrUpdateSensorRequest ApiRequest => this.ToApi();

        internal string GetBarOptionsInfo() => $"Bar period is {BarPeriod.ToReadableView()} with updates every {BarTickPeriod.ToReadableView()}.";
    }
}
