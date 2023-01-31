using System;

namespace HSMDataCollector.Options
{
    internal sealed class CollectorAliveOptions : OptionsProperty<SensorOptions>
    {
        protected override string NodePath { get; } = "System monitoring";


        internal CollectorAliveOptions() : base()
        {
            DefaultOptions.PostDataPeriod = TimeSpan.FromSeconds(15);
        }
    }
}
