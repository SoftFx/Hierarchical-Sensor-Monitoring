using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.Data;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal sealed class PackageContentSizeSensor : DoubleBarPublicSensor
    {
        public PackageContentSizeSensor(BarSensorOptions options) : base(options) { }


        internal void AddValue(PackageSendingInfo info)
        {
            // KILOBYTES (#1459): a real package is a couple of KB, and the bar's 2-decimal display
            // precision turned every megabyte reading into a flat 0.00.
            var contentSize = (info.ContentSize * sizeof(char)).BytesToKilobytesDouble();

            AddValue(contentSize);
        }
    }
}