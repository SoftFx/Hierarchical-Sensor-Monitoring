using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors
{
    internal abstract class FreeDiskSpaceBase : MonitoringSensorBase<double>
    {
        private readonly IDiskInfo _diskInfo;


        protected override string SensorName => $"Free space on disk{_diskInfo.Name} MB";


        internal FreeDiskSpaceBase(SensorOptions options, IDiskInfo diskInfo) : base(options)
        {
            _diskInfo = diskInfo;
        }


        protected sealed override double GetValue() => _diskInfo.FreeSpace.ToMegabytes();
    }
}
