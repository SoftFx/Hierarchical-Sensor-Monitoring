using HSMDataCollector.Alerts;
using HSMDataCollector.Converters;
using HSMDataCollector.Extensions;
using HSMDataCollector.Prototypes;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Options
{
    public interface IMonitoringOptions
    {
        TimeSpan PostDataPeriod { get; set; }
    }


    public abstract class SensorOptions
    {
        internal abstract AddOrUpdateSensorRequest ApiRequest { get; }

        internal SensorType Type { get; set; }


        internal string ComputerName { get; set; }

        internal string Module { get; set; }

        internal string Path { get; set; }


        public bool IsComputerSensor { get; set; } // singltone options sets dy default and sensor adds to .computer node

        public bool IsPrioritySensor { get; set; } // data sends in separate request


        public SpecialAlertTemplate TtlAlert { get; set; }


        public string Description { get; set; }

        public Unit? SensorUnit { get; set; }


        public TimeSpan? KeepHistory { get; set; }

        public TimeSpan? SelfDestroy { get; set; }

        public TimeSpan? TTL { get; set; }


        public StatisticsOptions Statistics { get; set; }

        public bool? EnableForGrafana { get; set; }

        public bool? IsSingletonSensor { get; set; }

        public bool? AggregateData { get; set; }


        public DefaultAlertsOptions DefaultAlertsOptions { get; set; }

        public bool IsForceUpdate { get; set; } // if true then DataCollector can chage user settings


        internal string CalculateSystemPath()
        {
            var computer = ComputerName;
            var module = IsComputerSensor ? null : Module;

            if (string.IsNullOrEmpty(computer))
            {
                computer = module;
                module = null;
            }

            return DefaultPrototype.BuildPath(computer, module, Path);
        }

        internal object Copy() => MemberwiseClone();
    }


    public class FileSensorOptions : InstantSensorOptions
    {
        public string DefaultFileName { get; set; }

        public string Extension { get; set; }
    }


    public class InstantSensorOptions : SensorOptions
    {
        public List<InstantAlertTemplate> Alerts { get; set; }

        internal override AddOrUpdateSensorRequest ApiRequest => this.ToApi();
    }


    public class MonitoringInstantSensorOptions : InstantSensorOptions, IMonitoringOptions
    {
        public virtual TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromSeconds(15);
    }


    public class CounterSensorOptions : MonitoringInstantSensorOptions
    {
        public override TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromMinutes(1);
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


    public class BarSensorOptions : SensorOptions, IMonitoringOptions
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