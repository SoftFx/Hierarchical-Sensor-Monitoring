using HSMDataCollector.Alerts;
using HSMDataCollector.Converters;
using HSMDataCollector.Prototypes;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;
using HSMDataCollector.Extensions;

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

        internal bool IsComputerSensor { get; set; }

        internal bool IsPrioritySensor { get; set; }


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
    }


    public class InstantSensorOptions : SensorOptions
    {
        public List<InstantAlertTemplate> Alerts { get; set; }

        internal override AddOrUpdateSensorRequest ApiRequest => this.ToApi();
    }


    public class MonitoringInstantSensorOptions : InstantSensorOptions, IMonitoringOptions
    {
        public TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromSeconds(15);
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