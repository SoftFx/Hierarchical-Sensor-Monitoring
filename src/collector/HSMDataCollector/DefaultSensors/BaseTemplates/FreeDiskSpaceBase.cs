using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class FreeDiskSpaceBase : MonitoringSensorBase<double>
    {
        private readonly IDiskInfo _diskInfo;


        internal FreeDiskSpaceBase(DiskSensorOptions options) : base(options)
        {
            _diskInfo = options.DiskInfo;
        }


        protected sealed override double GetValue() => _diskInfo.FreeSpaceMb;
    }
}