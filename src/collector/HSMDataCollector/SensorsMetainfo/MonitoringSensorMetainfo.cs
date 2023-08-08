using System;

namespace HSMDataCollector.SensorsMetainfo
{
    internal class BarMonitoringSensorMetainfo : SensorMetainfo
    {
        public TimeSpan CollectBarPeriod { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan BarPeriod { get; set; } = TimeSpan.FromMinutes(5);

        public int Precision { get; set; } = 2;
    }


    internal class DiskMonitoringSensorMetainfo : SensorMetainfo
    {
        internal TimeSpan PostDataPeriod { get; set; } = TimeSpan.FromMinutes(5);

        public int CalibrationRequests { get; set; } = 6;

        public string TargetPath { get; set; } = @"C:\";
    }
}