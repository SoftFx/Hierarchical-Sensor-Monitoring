using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.SyncQueue.Data;
using HSMDataCollector.Tables;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMTableBlocks;

#if NET5_0_OR_GREATER
#nullable disable
#endif

namespace HSMDataCollector.Tests.Tables
{
    /// <summary>
    /// A sender that carries tables, for the tests of the table pipeline: it accepts every value send, answers table
    /// registrations and blocks as the test scripts, and records what it got (registrations as sent, blocks decoded).
    /// </summary>
    internal sealed class FakeTableSender : IDataSender, ITableBlockChannel
    {
        private readonly object _lock = new object();
        private readonly List<IReadOnlyList<AddOrUpdateSensorRequest>> _registrations = new List<IReadOnlyList<AddOrUpdateSensorRequest>>();
        private readonly List<SentBlock> _blocks = new List<SentBlock>();
        private readonly ConcurrentDictionary<string, string> _refusedPaths = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<Func<SentBlock, TableBlockAck>> _scriptedAcks = new ConcurrentQueue<Func<SentBlock, TableBlockAck>>();

        private int _failRegistrations;
        private int _down;


        public int MaxBlockBytes { get; set; } = 4 * 1024 * 1024 - 40;

        public TableBlockLimits BlockLimits { get; set; } = TableBlockLimits.Default;

        public string TablesUnavailableReason => null;


        /// <summary>Every registration request, as sent.</summary>
        public IReadOnlyList<IReadOnlyList<AddOrUpdateSensorRequest>> Registrations
        {
            get
            {
                lock (_lock)
                    return _registrations.ToList();
            }
        }

        /// <summary>Every block put, delivered or not, in the order of the attempts.</summary>
        public IReadOnlyList<SentBlock> Attempts
        {
            get
            {
                lock (_lock)
                    return _blocks.ToList();
            }
        }

        /// <summary>The blocks answered stored, in order.</summary>
        public IReadOnlyList<SentBlock> Stored => Attempts.Where(b => b.Outcome == TableBlockOutcome.Stored).ToList();

        /// <summary>The rows of <paramref name="path"/> over every stored block, in order.</summary>
        public IReadOnlyList<Row> RowsOf(string path) => Stored.SelectMany(b => b.RowsOf(path)).ToList();


        /// <summary>The registration of <paramref name="path"/> is refused with <paramref name="reason"/>.</summary>
        public void RefuseRegistration(string path, string reason) => _refusedPaths[path] = reason;

        /// <summary>The next <paramref name="count"/> registration requests get no answer.</summary>
        public void FailRegistrations(int count) => Interlocked.Add(ref _failRegistrations, count);

        /// <summary>While down, every block is not delivered.</summary>
        public void SetDown(bool down) => Volatile.Write(ref _down, down ? 1 : 0);

        /// <summary>The next block is answered by <paramref name="ack"/> (one per call, in order).</summary>
        public void AnswerNext(Func<SentBlock, TableBlockAck> ack) => _scriptedAcks.Enqueue(ack);


        public Task WaitUntilReadyAsync(CancellationToken token) => Task.CompletedTask;

        public Task<TableRegistrationResult> RegisterTablesAsync(IReadOnlyList<AddOrUpdateSensorRequest> registrations, CancellationToken token)
        {
            lock (_lock)
                _registrations.Add(registrations.ToList());

            if (Interlocked.Decrement(ref _failRegistrations) >= 0)
                return Task.FromResult(TableRegistrationResult.NotSent("scripted: no answer"));

            Interlocked.Exchange(ref _failRegistrations, 0);

            var refused = registrations.Where(r => _refusedPaths.ContainsKey(r.Path)).ToDictionary(r => r.Path, r => _refusedPaths[r.Path]);
            var accepted = registrations.Where(r => !refused.ContainsKey(r.Path)).Select(r => r.Path);

            return Task.FromResult(new TableRegistrationResult(accepted, refused, null));
        }

        public Task<TableBlockAck> PutBlockAsync(Guid producer, long sequence, byte[] block, CancellationToken token)
        {
            var sent = new SentBlock(producer, sequence, block);

            TableBlockAck ack;

            if (_scriptedAcks.TryDequeue(out var scripted))
                ack = scripted(sent);
            else if (Volatile.Read(ref _down) == 1)
                ack = TableBlockAck.NotDelivered("scripted: down");
            else
                ack = TableBlockAck.Stored();

            sent.Outcome = ack.Outcome;
            sent.Refused = new HashSet<int>(ack.Refused.Select(r => r.Index));

            lock (_lock)
                _blocks.Add(sent);

            return Task.FromResult(ack);
        }


        public ValueTask<ConnectionResult> TestConnectionAsync() => new ValueTask<ConnectionResult>(ConnectionResult.Ok);

        public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => Ok();

        public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) => Ok();

        public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) => Ok();

        public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) => Ok();

        public void Dispose() { }

        private static ValueTask<PackageSendingInfo> Ok() => new ValueTask<PackageSendingInfo>(new PackageSendingInfo(0, 200, null));


        internal readonly struct Row
        {
            internal Row(long ticks, double?[] values)
            {
                Ticks = ticks;
                Values = values;
            }

            internal long Ticks { get; }

            internal double?[] Values { get; }
        }

        /// <summary>One block as the server would read it.</summary>
        internal sealed class SentBlock
        {
            internal SentBlock(Guid producer, long sequence, byte[] bytes)
            {
                Producer = producer;
                Sequence = sequence;
                Bytes = bytes;
                Block = TableBlockReader.Read(bytes);
            }

            internal Guid Producer { get; }

            internal long Sequence { get; }

            internal byte[] Bytes { get; }

            internal TableBlock Block { get; }

            internal TableBlockOutcome Outcome { get; set; }

            /// <summary>The directory indexes of the tables the answer refused: their rows were not stored.</summary>
            internal ISet<int> Refused { get; set; } = new HashSet<int>();

            internal IReadOnlyList<string> TableNames => Block.Tables.Select(t => Encoding.UTF8.GetString(t.TableId)).ToList();

            internal IEnumerable<Row> RowsOf(string path)
            {
                var index = TableNames.ToList().IndexOf(path);

                if (index < 0 || Refused.Contains(index))
                    yield break;

                var table = Block.Tables[index];

                if (table == null)
                    yield break;

                for (var row = 0; row < table.RowCount; row++)
                {
                    var values = new double?[table.Columns.Count - 1];

                    for (var column = 1; column < table.Columns.Count; column++)
                    {
                        var data = table.Columns[column];

                        values[column - 1] = data.IsNull(row) ? (double?)null
                            : data.Type == HSMTableBlocks.TableColumnType.Double ? data.DoubleValues[row]
                            : data.Int64Values[row];
                    }

                    yield return new Row(table.Time[row], values);
                }
            }
        }
    }
}
