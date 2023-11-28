using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal sealed class PackageContentSizeSensor : DoubleBarPublicSensor
    {
        public PackageContentSizeSensor(BarSensorOptions options) : base(options) { }


        internal void AddValue(PackageSendingInfo info)
        {
            var contentSize = (info.ContentSize * sizeof(char)).BytesToMegabytesDouble();

            AddValue(contentSize);
        }
    }
}