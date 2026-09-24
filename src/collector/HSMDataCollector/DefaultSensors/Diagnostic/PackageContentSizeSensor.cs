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
            //
            // ContentSize is the serialized body's CHAR count, and the body goes on the wire as
            // UTF-8 (StringContent(..., Encoding.UTF8)), so one char is one byte for the ASCII
            // JSON this collector produces. The old * sizeof(char) measured the in-memory UTF-16
            // string instead and reported twice the package that was actually sent — invisible
            // while the sensor was stuck at 0.00 in MB, and a 2x divergence from the native
            // collector (which sums the UTF-8 bytes it sends) the moment the sensor reads
            // non-zero. Rule #10: one sensor, one quantity.
            var contentSize = info.ContentSize.BytesToKilobytesDouble();

            AddValue(contentSize);
        }
    }
}