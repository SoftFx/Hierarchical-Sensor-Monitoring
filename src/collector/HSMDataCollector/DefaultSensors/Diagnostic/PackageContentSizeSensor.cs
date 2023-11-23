using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.Sensors;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal sealed class PackageContentSizeSensor : SensorInstant<double>
    {
        public PackageContentSizeSensor(SensorOptions options) : base(options) { }


        internal void AddValue(PackageSendingInfo info)
        {
            var contentSize = (info.ContentSize * sizeof(char)).BytesToMegabytesDouble();

            AddValue(contentSize, info.IsSuccess ? SensorStatus.Ok : SensorStatus.Error, info.Error);
        }
    }
}