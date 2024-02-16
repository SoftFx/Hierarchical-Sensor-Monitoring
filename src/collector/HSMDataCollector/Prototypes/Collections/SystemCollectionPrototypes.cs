using HSMDataCollector.Alerts;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Prototypes
{
    internal abstract class SystemMonitoringPrototype : BarSensorOptionsPrototype<BarSensorOptions>
    {
        protected SystemMonitoringPrototype() : base()
        {
            Type = SensorType.DoubleBarSensor;

            IsComputerSensor = true;
        }
    }


    internal sealed class FreeRamMemoryPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Free RAM memory";

        public FreeRamMemoryPrototype() : base()
        {
            Description = "Free memory, which is memory available to the operating system," +
            " is defined as free and cache pages. The remainder is active memory, which is memory " +
            $"currently in use by the operating system. {GetBarOptionsInfo()}" +
            "More info can be found [**here**](https://en.wikipedia.org/wiki/Random-access_memory).";

            SensorUnit = Unit.MB;
        }
    }


    internal sealed class TotalCPUPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Total CPU";


        public TotalCPUPrototype() : base()
        {
            Description = "CPU usage indicates the total percentage of processing power" +
            " exhausted to process data and run various programs on a network device, " +
            $"server, or computer at any given point. {GetBarOptionsInfo()}" +
            "More info can be found [**here**](https://en.wikipedia.org/wiki/Central_processing_unit).";

            SensorUnit = Unit.Percents;
            Statistics = StatisticsOptions.EMA;

            Alerts = new List<BarAlertTemplate>()
            {
                AlertsFactory.IfEmaMean(AlertOperation.GreaterThan, 50)
                             .ThenSendNotification("[$product]$path $property $operation $target$unit")
                             .AndSetIcon(AlertIcon.Warning).Build(),
            };
        }
    }


    internal sealed class TimeInGCPrototype : SystemMonitoringPrototype
    {
        protected override string SensorName => "Time in GC";


        public TimeInGCPrototype() : base()
        {
            SensorUnit = Unit.Percents;
        }


        public override BarSensorOptions Get(BarSensorOptions customOptions)
        {
            var options = base.Get(customOptions);

            options.Description = string.Format(BaseDescription, SensorName, options.PostDataPeriod.ToReadableView(), options.BarPeriod.ToReadableView(), $"{WindowsTimeInGCBase.Category}/{WindowsTimeInGCBase.Counter}");

            return options;
        }
    }
}