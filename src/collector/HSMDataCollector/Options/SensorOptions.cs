using System;
using System.Collections.Generic;
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

        public SpecialAlertTemplate TtlAlert { get; set; }

        public string Description { get; set; }

        public Unit? SensorUnit { get; set; }


        [Obsolete("This setting doesn't exist for sensor now")]
        public DefaultChatsMode? DefaultChats { get; set; }

        public TimeSpan? KeepHistory { get; set; }

        public TimeSpan? SelfDestroy { get; set; }

        public TimeSpan? TTL { get; set; }

        public StatisticsOptions Statistics { get; set; }

        public bool? EnableForGrafana { get; set; }

        public bool? IsSingletonSensor { get; set; }

        public bool? AggregateData { get; set; }

        public DefaultAlertsOptions DefaultAlertsOptions { get; set; }

        public bool IsForceUpdate { get; set; } // if true then DataCollector can chage user settings

        public SensorLocation SensorLocation { get; set; }

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
        public string DefaultFileName { get; set; }

        public string Extension { get; set; }
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