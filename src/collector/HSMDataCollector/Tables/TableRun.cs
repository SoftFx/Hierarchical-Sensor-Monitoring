using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Threading;

namespace HSMDataCollector.Tables
{
    /// <summary>
    /// Everything one run of the table pipeline owns, from a <c>Start</c> to the end of its stop: its cancellation, the
    /// packing and delivery gates, the backlog and the block in flight, the producer id and sequence, the bytes reserved
    /// by writers, the registration marks and the loss ledger.
    /// <para>
    /// A sensor holds a reference to the run it belongs to now; every operation of a call (a row added, a tick, a
    /// registration answer, a delivery) is done against the one run object it started with. A retired run refuses rows,
    /// packs nothing and enqueues nothing, so nothing of an old run can reach a later one: an old continuation can only
    /// touch its own retired object. No generation numbers are compared.
    /// </para>
    /// </summary>
    internal sealed class TableRun
    {
        private const int Open = 0, Draining = 1, Closed = 2;

        private readonly long _maxBufferBytes;
        private readonly Func<Action> _reservationHook;
        private int _phase;
        private long _bufferedBytes;

        internal TableRun(long number, long maxBufferBytes, Func<Action> reservationHook = null)
        {
            Number = number;
            Producer = Guid.NewGuid();
            Token = Cancellation.Token;
            _maxBufferBytes = maxBufferBytes;
            _reservationHook = reservationHook;
        }

        /// <summary>The run's number, rising at every Start (for logs and tests; never compared to decide anything).</summary>
        internal long Number { get; }

        /// <summary>The producer id of this run's blocks: new at every Start.</summary>
        internal Guid Producer { get; }

        internal CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        /// <summary>The run's token, taken at construction: it stays readable after the source is disposed at the close.</summary>
        internal CancellationToken Token { get; }

        /// <summary>One pack at a time: a tick's, or the stop's final one.</summary>
        internal SemaphoreSlim PackGate { get; } = new SemaphoreSlim(1, 1);

        /// <summary>One delivery at a time: the loop's, or the stop's.</summary>
        internal SemaphoreSlim DeliveryGate { get; } = new SemaphoreSlim(1, 1);

        internal SemaphoreSlim DeliverySignal { get; } = new SemaphoreSlim(0, int.MaxValue);

        /// <summary>Guards <see cref="Backlog"/>, <see cref="InFlight"/> and <see cref="BacklogBytes"/>.</summary>
        internal object BacklogLock { get; } = new object();

        internal LinkedList<PendingBlock> Backlog { get; } = new LinkedList<PendingBlock>();

        /// <summary>The block being put; out of reach of the backlog bound until its answer is applied.</summary>
        internal PendingBlock InFlight { get; set; }

        internal long BacklogBytes;

        /// <summary>The next producer sequence; written under <see cref="PackGate"/> only.</summary>
        internal long NextSequence;

        internal long RetryNotBeforeTicks;

        internal ScheduledTask Tick { get; set; }

        internal Task DeliveryLoop { get; set; }

        /// <summary>The sensors a registration request of this run carries now.</summary>
        internal ConcurrentDictionary<TableSensor, byte> Registering { get; } = new ConcurrentDictionary<TableSensor, byte>();

        internal TableLossLedger Ledger { get; } = new TableLossLedger();

        /// <summary>True from Start until the stop begins its final pack: rows are taken.</summary>
        internal bool AcceptsRows => Volatile.Read(ref _phase) == Open;

        /// <summary>True once the stop has ended the run: nothing is packed or enqueued any more.</summary>
        internal bool IsClosed => Volatile.Read(ref _phase) == Closed;

        /// <summary>The bytes writers of this run hold in the buffers of its sensors.</summary>
        internal long BufferedBytes => Interlocked.Read(ref _bufferedBytes);

        /// <summary>The stop begins: rows are refused from now on, the final pack and delivery may still run.</summary>
        internal void BeginDrain() => Interlocked.CompareExchange(ref _phase, Draining, Open);

        /// <summary>The stop is over: the run takes, packs and enqueues nothing more.</summary>
        internal void Close()
        {
            Volatile.Write(ref _phase, Closed);

            try
            {
                Cancellation.Cancel();
                Cancellation.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Takes <paramref name="bytes"/> of the bound, or nothing. A compare-and-swap that never overshoots: a request
        /// that cannot fit is refused before it touches the counter.
        /// </summary>
        internal bool TryReserve(long bytes)
        {
            try
            {
                if (bytes > _maxBufferBytes)
                    return false;

                while (true)
                {
                    var current = Interlocked.Read(ref _bufferedBytes);

                    if (current + bytes > _maxBufferBytes)
                        return false;

                    if (Interlocked.CompareExchange(ref _bufferedBytes, current + bytes, current) == current)
                        return true;
                }
            }
            finally
            {
                _reservationHook?.Invoke()?.Invoke();
            }
        }

        internal void Release(long bytes) => Interlocked.Add(ref _bufferedBytes, -bytes);
    }


    /// <summary>
    /// The lost rows of one run, in one place (no silent loss, rule 8). Every drop adds here and to the sensor's own
    /// <c>RowsDropped</c>. The unreported total is handed to the <c>Queue overflow</c> sensor while that sensor takes
    /// values; what it never took is said in one line at the very end of the stop (<see cref="Finish"/>). After that a
    /// drop is counted on its sensor only. The words of each reason are reported apart, once per tick.
    /// Thread-safe; its lock guards counters only and nothing is reported under it.
    /// </summary>
    internal sealed class TableLossLedger
    {
        private static readonly int ReasonCount = Enum.GetValues(typeof(TableDropReason)).Length;

        private readonly object _lock = new object();
        private readonly long[] _unworded = new long[ReasonCount];
        private readonly long[] _byReason = new long[ReasonCount];
        private long _unreported;
        private long _total;
        private bool _finished;

        /// <summary>Every row this run lost, reported or not.</summary>
        internal long Total
        {
            get
            {
                lock (_lock)
                    return _total;
            }
        }

        /// <summary>Every row this run lost for <paramref name="reason"/>.</summary>
        internal long TotalOf(TableDropReason reason)
        {
            lock (_lock)
                return _byReason[(int)reason];
        }

        /// <summary>The rows not yet in a bar of the <c>Queue overflow</c> sensor.</summary>
        internal long Unreported
        {
            get
            {
                lock (_lock)
                    return _unreported;
            }
        }

        internal void Add(long rows, TableDropReason reason)
        {
            if (rows <= 0)
                return;

            lock (_lock)
            {
                if (_finished)
                    return;

                _unworded[(int)reason] += rows;
                _byReason[(int)reason] += rows;
                _unreported += rows;
                _total += rows;
            }
        }

        /// <summary>The reasons with rows lost since the last call, each once.</summary>
        internal List<TableDropReason> TakeReasons()
        {
            var reasons = new List<TableDropReason>();

            lock (_lock)
                for (var reason = 1; reason < ReasonCount; reason++)
                    if (_unworded[reason] > 0)
                    {
                        _unworded[reason] = 0;
                        reasons.Add((TableDropReason)reason);
                    }

            return reasons;
        }

        /// <summary>Takes the unreported rows to hand them to the overflow sensor; <see cref="GiveBack"/> when it refuses them.</summary>
        internal long TakeUnreported()
        {
            lock (_lock)
            {
                var rows = _unreported;
                _unreported = 0;
                return rows;
            }
        }

        internal void GiveBack(long rows)
        {
            if (rows <= 0)
                return;

            lock (_lock)
                if (!_finished)
                    _unreported += rows;
        }

        /// <summary>Closes the ledger and returns the rows that are in no published bar. Called once, at the end of the stop.</summary>
        internal long Finish()
        {
            lock (_lock)
            {
                if (_finished)
                    return 0;

                _finished = true;

                var rows = _unreported;
                _unreported = 0;
                return rows;
            }
        }
    }


    internal readonly struct PendingTable
    {
        internal PendingTable(TableSensor sensor, int rows)
        {
            Sensor = sensor;
            Rows = rows;
        }

        internal TableSensor Sensor { get; }

        internal int Rows { get; }
    }


    /// <summary>A packed block waiting for its final answer; its tables in directory order.</summary>
    internal sealed class PendingBlock
    {
        internal PendingBlock(TableRun run, long sequence, byte[] bytes, PendingTable[] tables, int rows)
        {
            Run = run;
            Sequence = sequence;
            Bytes = bytes;
            Tables = tables;
            Rows = rows;
        }

        internal TableRun Run { get; }

        internal long Sequence { get; }

        internal byte[] Bytes { get; }

        internal PendingTable[] Tables { get; }

        internal int Rows { get; }

        internal void DropRows(TableDropReason reason)
        {
            foreach (var table in Tables)
                table.Sensor.Drop(table.Rows, reason, Run);
        }
    }
}
