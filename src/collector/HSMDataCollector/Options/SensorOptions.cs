using System;

namespace HSMDataCollector.Options
{
    public class SensorOptions
    {
        public string NodePath { get; set; }

        public TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromMinutes(5);


        public SensorOptions(string nodePath)
        {
            NodePath = nodePath;
        }
    }


    public sealed class BarSensorOptions : SensorOptions
    {
        public TimeSpan CollectBarPeriod { get; set; } = TimeSpan.FromSeconds(5);


        public BarSensorOptions(string nodePath) : base(nodePath) { }
    }


    public sealed class WindowsSensorOptions : SensorOptions
    {
        public TimeSpan AcceptableUpdateInterval { get; set; } = TimeSpan.FromDays(30);


        public WindowsSensorOptions(string nodePath) : base(nodePath) { }
    }
}
