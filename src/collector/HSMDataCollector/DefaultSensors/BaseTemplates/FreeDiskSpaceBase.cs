using System;
using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class FreeDiskSpaceBase : MonitoringSensorBase<double, NoDisplayUnit>
    {
        private readonly IDiskInfo _diskInfo;
        private Exception _lastReadException;


        internal FreeDiskSpaceBase(DiskSensorOptions options) : base(options)
        {
            _diskInfo = options.DiskInfo;
        }


        protected sealed override double GetValue()
        {
            try
            {
                var freeSpaceMb = _diskInfo.FreeSpaceMb;
                _lastReadException = null;

                return freeSpaceMb;
            }
            catch (Exception ex)
            {
                _lastReadException = ex;
                HandleException(ex);

                return default;
            }
        }

        protected sealed override string GetComment() => _lastReadException?.Message;

        protected sealed override SensorStatus GetStatus() => _lastReadException == null ? SensorStatus.Ok : SensorStatus.Error;
    }
}
