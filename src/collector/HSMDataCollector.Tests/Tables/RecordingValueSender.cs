using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;

#if NET5_0_OR_GREATER
#nullable disable
#endif

namespace HSMDataCollector.Tests.Tables
{
    /// <summary>
    /// A sender without a table transport (a custom <c>CollectorOptions.DataSender</c>, as an HTTP collector is to the
    /// tables): it accepts every send and records the values, so a test reads what a sensor published.
    /// </summary>
    internal sealed class RecordingValueSender : IDataSender
    {
        private readonly object _lock = new object();
        private readonly List<SensorValueBase> _values = new List<SensorValueBase>();

        /// <summary>Every value sent, data and priority, in the order of the sends.</summary>
        public IReadOnlyList<SensorValueBase> Values
        {
            get
            {
                lock (_lock)
                    return _values.ToList();
            }
        }

        /// <summary>The bars the <c>Queue overflow</c> sensor published, in order.</summary>
        public IReadOnlyList<BarSensorValueBase<int>> OverflowBars =>
            Values.OfType<BarSensorValueBase<int>>().Where(v => v.Path != null && v.Path.EndsWith("Queue overflow", System.StringComparison.Ordinal)).ToList();

        public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

        public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => Record(items);

        public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => Record(items);

        public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => Ok();

        public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => Ok();

        public void Dispose() { }

        private ValueTask<PackageSendingInfo> Record(IEnumerable<SensorValueBase> items)
        {
            lock (_lock)
                _values.AddRange(items);

            return Ok();
        }

        private static ValueTask<PackageSendingInfo> Ok() => new ValueTask<PackageSendingInfo>(new PackageSendingInfo(0, 200, null));
    }
}
