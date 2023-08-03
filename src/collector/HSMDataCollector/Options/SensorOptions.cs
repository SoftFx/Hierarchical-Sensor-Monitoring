using NLog.LayoutRenderers.Wrappers;
using System;
using System.Collections.Generic;
using System.Text;

namespace HSMDataCollector.Options
{
    public enum Units
    {
        bits = 0,
        bytes = 1,
        KB = 2,
        MB = 3,
        GB = 4,

        Percents = 100,
    }

    internal class SensorMetainfoUnits
    {
        internal Units SelectedUnit { get; set; }
        
        internal Units[] AvailableUnits { get; set; }
    }

    internal class SensorMetainfoSettings
    {
        internal TimeSpan TTL { get; set; }

        internal TimeSpan SaveSensorHistory { get; set; }
        
        internal TimeSpan SelfDestroy { get; set; }
    }


    internal class SensorMetainfoEnables
    {
        internal bool ForGrafana { get; set; }
    }


    internal class SensorMetainfo
    {
        internal string Path { get; set; }

        internal string Description { get; set; }

        internal bool OnlyUniqValues { get; set; }

        internal SensorMetainfoSettings Settings { get; } = new SensorMetainfoSettings();

        internal SensorMetainfoEnables Enables { get; } = new SensorMetainfoEnables();

        internal SensorMetainfoUnits Units { get; } = new SensorMetainfoUnits();
    }
}
