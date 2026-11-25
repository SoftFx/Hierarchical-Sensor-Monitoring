#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMTableBlocks;

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// The table connection of the scripted server (#1845, wire contract "Table connection"): the upgrade with
    /// <c>hsm.tables.v1</c> and <c>X-Hsm-Session</c>, the table hello with settable limits, binary frames read as the
    /// server reads them (40-byte header, the block decoded with <see cref="TableBlockReader"/>), and an ack per frame as
    /// the test scripts it. Without a script a block is stored, and a block of a producer and sequence stored before is
    /// answered <c>duplicate</c>, as the server does.
    /// </summary>
    public sealed partial class ScriptedSessionServer
    {
        public const string TablesSubprotocol = "hsm.tables.v1";

        private readonly ConcurrentDictionary<string, int> _liveSessions = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _sessionIds = new ConcurrentQueue<string>();
        private readonly ConcurrentDictionary<int, TableSocket> _tableOpen = new ConcurrentDictionary<int, TableSocket>();
        private readonly ConcurrentQueue<TableFrame> _tableFrames = new ConcurrentQueue<TableFrame>();
        private readonly ConcurrentQueue<TableAnswer> _tableScript = new ConcurrentQueue<TableAnswer>();
        private readonly ConcurrentQueue<int> _tableCloses = new ConcurrentQueue<int>();
        private readonly ConcurrentQueue<Dictionary<string, string>> _tableUpgrades = new ConcurrentQueue<Dictionary<string, string>>();
        private readonly ConcurrentDictionary<string, TableFrame> _storedBlocks = new ConcurrentDictionary<string, TableFrame>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<long> _tableProbes = new ConcurrentQueue<long>();

        private int _tableRefuseStatus;
        private int _tableConnections;
        private int _refusedTableUpgrades;
        private int _tablePings;

        // By request id: completed when this server decided to leave that block frame without an answer (stored
        // silently, or not answered).
        private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>> _unanswered = new ConcurrentDictionary<long, TaskCompletionSource<bool>>();


        // --- the script ---

        /// <summary>The hello of the sessions accepted from now on carries a <c>sessionId</c> (true by default).</summary>
        public bool SendSessionId { get; set; } = true;

        /// <summary>What the table hello of the table connections accepted from now on states.</summary>
        public int TableMaxFrameBytes { get; set; } = 4 * 1024 * 1024;

        public int TableMaxRows { get; set; } = TableBlockLimits.DefaultMaxRows;

        public int TableMaxTables { get; set; } = TableBlockLimits.DefaultMaxTables;

        /// <summary>The table hello is sent as this text instead, when set (a broken server).</summary>
        public string TableHelloOverride { get; set; }

        /// <summary>A table upgrade whose <c>X-Hsm-Session</c> names no live session is refused with 409, as the server does.</summary>
        public bool CheckTableSession { get; set; } = true;

        /// <summary>Every table upgrade from now on is refused with this HTTP status; sessions are not touched.</summary>
        public void RefuseTableUpgrades(int status) => Volatile.Write(ref _tableRefuseStatus, status);

        public void AcceptTableUpgrades() => Volatile.Write(ref _tableRefuseStatus, 0);

        /// <summary>The next block frame is answered as <paramref name="answer"/> says (one per call, in order).</summary>
        public void AnswerTableNext(TableAnswer answer) => _tableScript.Enqueue(answer);

        /// <summary>Closes every open table connection from the server's side with this code and reason.</summary>
        public void CloseTableConnections(int code, string reason = "")
        {
            foreach (var table in _tableOpen.Values.ToArray())
                table.CloseAsync(code, reason).GetAwaiter().GetResult();
        }

        /// <summary>Ends every open table connection without a close handshake.</summary>
        public void DropTableConnections()
        {
            foreach (var table in _tableOpen.Values.ToArray())
                try { table.Socket.Abort(); } catch (Exception) { }
        }


        // --- what the collector did ---

        /// <summary>The <c>sessionId</c> of every session accepted so far, in order.</summary>
        public IReadOnlyList<string> SessionIds => _sessionIds.ToArray();

        /// <summary>Table upgrades accepted so far.</summary>
        public int TableConnections => Volatile.Read(ref _tableConnections);

        public int RefusedTableUpgrades => Volatile.Read(ref _refusedTableUpgrades);

        public int TablePings => Volatile.Read(ref _tablePings);

        /// <summary>The request headers of every table upgrade the server saw (accepted or refused), in order.</summary>
        public IReadOnlyList<Dictionary<string, string>> TableUpgradeHeaders => _tableUpgrades.ToArray();

        /// <summary>The request ids of the probes received, in order.</summary>
        public IReadOnlyList<long> TableProbes => _tableProbes.ToArray();

        /// <summary>Every block frame received, in order of arrival, with what it was answered.</summary>
        public IReadOnlyList<TableFrame> TableFrames => _tableFrames.ToArray();

        /// <summary>The close codes the collector sent on table connections, in order.</summary>
        public IReadOnlyList<int> TableCloses => _tableCloses.ToArray();

        /// <summary>
        /// Completes once this server has decided to leave the block frame of request <paramref name="id"/> without an
        /// answer by its script (stored silently, or not answered): the moment a collector's wait for that ack can end as
        /// "no ack in time" without a clock deciding it. Never completes for a frame that is answered.
        /// </summary>
        public Task WhenTableFrameUnanswered(long id) => Unanswered(id).Task;

        private TaskCompletionSource<bool> Unanswered(long id) =>
            _unanswered.GetOrAdd(id, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));

        /// <summary>
        /// Runs when the collector's close frame of a session arrives, before it is answered. The order in which this
        /// server reads the close frames of two sockets says nothing about the order they were sent in, so a test that
        /// asks "was the table connection closed first" reads the collector's own state here: at this moment the
        /// collector has sent the session's close, and must have closed its table connection before.
        /// </summary>
        public Action SessionCloseReceived { get; set; }

        /// <summary>The blocks the server stored (each once), in order of their first frame.</summary>
        public IReadOnlyList<TableFrame> StoredTableBlocks => TableFrames.Where(f => f.Stored).ToList();

        /// <summary>The rows of <paramref name="path"/> over every stored block, refused tables left out: time ticks and the values.</summary>
        public IReadOnlyList<(long Ticks, double?[] Values)> StoredTableRowsOf(string path) =>
            StoredTableBlocks.SelectMany(f => f.RowsOf(path)).ToList();


        // --- the server side ---

        private static bool IsTableUpgrade(HttpListenerContext context)
        {
            var offered = context.Request.Headers["Sec-WebSocket-Protocol"] ?? string.Empty;

            return offered.Split(',').Any(p => p.Trim() == TablesSubprotocol);
        }

        private async Task HandleTableUpgradeAsync(HttpListenerContext context, Dictionary<string, string> headers)
        {
            _tableUpgrades.Enqueue(headers);

            var refuse = Volatile.Read(ref _tableRefuseStatus);

            if (refuse == 0 && CheckTableSession)
            {
                headers.TryGetValue("X-Hsm-Session", out var named);

                if (named == null || !_liveSessions.ContainsKey(named))
                    refuse = 409;
            }

            if (refuse != 0)
            {
                Interlocked.Increment(ref _refusedTableUpgrades);
                context.Response.StatusCode = refuse;
                context.Response.Close();
                return;
            }

            headers.TryGetValue("X-Hsm-Session", out var sessionId);

            var upgraded = await context.AcceptWebSocketAsync(TablesSubprotocol).ConfigureAwait(false);
            var index = Interlocked.Increment(ref _tableConnections);
            var table = new TableSocket(upgraded.WebSocket, index, sessionId);

            _tableOpen[index] = table;

            try
            {
                await ServeTableAsync(table).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A connection that ends badly is part of what the tests script.
            }
            finally
            {
                _tableOpen.TryRemove(index, out _);
                table.Socket.Dispose();
            }
        }

        // As the server: a table connection is closed with 1000 when its session ends.
        private void CloseTableConnectionOf(string sessionId)
        {
            foreach (var table in _tableOpen.Values.Where(t => t.SessionId == sessionId).ToArray())
                _ = table.CloseAsync(1000, "its session ended");
        }

        private async Task ServeTableAsync(TableSocket table)
        {
            var hello = TableHelloOverride
                ?? $"{{\"op\":\"hello\",\"body\":{{\"maxFrameBytes\":{TableMaxFrameBytes},\"heartbeatSeconds\":{HeartbeatSeconds}," +
                   $"\"blockLimits\":{{\"maxDecompressedBytes\":{TableBlockLimits.DefaultMaxDecompressedBytes},\"maxTables\":{TableMaxTables},\"maxRows\":{TableMaxRows}," +
                   $"\"maxColumnsPerTable\":{TableBlockLimits.DefaultMaxColumnsPerTable},\"maxTableIdLength\":{TableBlockLimits.DefaultMaxTableIdLength}}}}}}}";

            await table.SendTextAsync(hello).ConfigureAwait(false);

            var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
            var socket = table.Socket;

            while (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseSent)
            {
                WebSocketReceiveResult result;
                byte[] message;

                using (var stream = new MemoryStream())
                {
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, _stop.Token).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _tableCloses.Enqueue((int)(result.CloseStatus ?? 0));

                            if (socket.State == WebSocketState.CloseReceived)
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);

                            return;
                        }

                        stream.Write(buffer.Array, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    message = stream.ToArray();
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    if (Encoding.UTF8.GetString(message).Contains("\"ping\""))
                    {
                        Interlocked.Increment(ref _tablePings);
                        await table.SendTextAsync("{\"op\":\"pong\"}").ConfigureAwait(false);
                    }

                    continue;
                }

                if (!await AnswerTableFrameAsync(table, message).ConfigureAwait(false))
                    return;
            }
        }

        // False when the connection is over.
        private async Task<bool> AnswerTableFrameAsync(TableSocket table, byte[] frame)
        {
            if (frame.Length < 40 || frame[0] != 1 || (frame[1] != 1 && frame[1] != 2) || frame[2] != 0 || frame[3] != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4)) != 40)
            {
                await table.CloseAsync(1002, "a binary frame without a trusted header").ConfigureAwait(false);
                return false;
            }

            var id = BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(8));

            if (frame[1] == 2)
            {
                _tableProbes.Enqueue(id);
                await table.SendTextAsync($"{{\"ack\":{id},\"status\":200,\"body\":{{}}}}").ConfigureAwait(false);
                return true;
            }

            if (frame.Length > TableMaxFrameBytes)
            {
                await table.CloseAsync(1009, "a frame over maxFrameBytes").ConfigureAwait(false);
                return false;
            }

            var received = new TableFrame(table.Index, id, new Guid(frame.AsSpan(16, 16).ToArray()), BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(32)),
                                          frame.AsSpan(40).ToArray());
            var key = $"{received.Producer:N}/{received.Sequence}";

            if (!_tableScript.TryDequeue(out var answer))
                answer = TableAnswer.Store();

            _tableFrames.Enqueue(received);

            switch (answer.Kind)
            {
                case TableAnswerKind.Store:
                    if (_storedBlocks.TryGetValue(key, out var first))
                    {
                        received.Outcome = "duplicate";
                        await table.SendTextAsync(BlockAck(id, "duplicate", first)).ConfigureAwait(false);
                        return true;
                    }

                    received.Refused = answer.RefuseTables;
                    received.Outcome = "stored";
                    received.Stored = true;
                    _storedBlocks[key] = received;

                    if (answer.Silent)
                    {
                        Unanswered(id).TrySetResult(true);
                        return true;
                    }

                    await table.SendTextAsync(BlockAck(id, "stored", received)).ConfigureAwait(false);
                    return true;

                case TableAnswerKind.Text:
                    received.Outcome = answer.Text;
                    await table.SendTextAsync(answer.Text.Replace("{id}", id.ToString())).ConfigureAwait(false);
                    return true;

                case TableAnswerKind.Status:
                    received.Outcome = answer.Status.ToString();
                    await table.SendTextAsync(answer.RetryAfter.HasValue
                        ? $"{{\"ack\":{id},\"status\":{answer.Status},\"error\":\"scripted\",\"retryAfter\":{answer.RetryAfter.Value}}}"
                        : $"{{\"ack\":{id},\"status\":{answer.Status},\"error\":\"scripted\"}}").ConfigureAwait(false);
                    return true;

                case TableAnswerKind.Silent:
                    received.Outcome = "no answer";
                    Unanswered(id).TrySetResult(true);
                    return true;

                case TableAnswerKind.Close:
                    received.Outcome = $"closed {answer.Status}";
                    await table.CloseAsync(answer.Status, answer.Text ?? string.Empty).ConfigureAwait(false);
                    return false;

                case TableAnswerKind.Binary:
                    received.Outcome = "binary";
                    await table.SendBinaryAsync(new byte[] { 1, 2, 3 }).ConfigureAwait(false);
                    return true;

                case TableAnswerKind.RefuseAll:
                    received.Outcome = "allRefused";
                    received.Refused = received.TableNames.ToDictionary(name => name, _ => answer.Text);
                    await table.SendTextAsync(BlockAck(id, "allRefused", received)).ConfigureAwait(false);
                    return true;

                case TableAnswerKind.DropSession:
                    // The session goes without a close handshake; its end closes this table connection with 1000
                    // "its session ended" (ServeAsync's finally), as the server does. The frame gets no answer.
                    received.Outcome = "session dropped";
                    DropSessions();
                    return true;

                default:
                    throw new InvalidOperationException(answer.Kind.ToString());
            }
        }

        // The ack of a stored block, with the refusals of the first put (a duplicate answers the first one's).
        private static string BlockAck(long id, string outcome, TableFrame stored)
        {
            var names = stored.TableNames;
            var refused = stored.Refused ?? new Dictionary<string, string>();
            var entries = names.Select((name, index) => (name, index)).Where(t => refused.ContainsKey(t.name)).ToList();
            var all = entries.Count == names.Count && names.Count > 0;

            var list = string.Join(",", entries.Select(t => $"{{\"index\":{t.index},\"table\":\"{t.name}\",\"reason\":\"{refused[t.name]}\"}}"));

            return $"{{\"ack\":{id},\"status\":200,\"body\":{{\"outcome\":\"{(all && outcome == "stored" ? "allRefused" : outcome)}\",\"tables\":{names.Count},\"refusedCount\":{entries.Count},\"refused\":[{list}]}}}}";
        }


        public enum TableAnswerKind
        {
            Store,
            Status,
            Text,
            Silent,
            Close,
            Binary,
            RefuseAll,
            DropSession,
        }

        /// <summary>How the scripted server answers one block frame.</summary>
        public sealed class TableAnswer
        {
            public TableAnswerKind Kind { get; private set; }

            public int Status { get; private set; }

            public int? RetryAfter { get; private set; }

            public string Text { get; private set; }

            public bool Silent { get; private set; }

            public Dictionary<string, string> RefuseTables { get; private set; }

            /// <summary>Stored (duplicate when stored before), the tables named refused with their reasons.</summary>
            public static TableAnswer Store(Dictionary<string, string> refuseTables = null) => new TableAnswer { Kind = TableAnswerKind.Store, RefuseTables = refuseTables };

            /// <summary>Stored, and no ack: the collector sees no answer, and its resend is answered duplicate.</summary>
            public static TableAnswer StoreSilently() => new TableAnswer { Kind = TableAnswerKind.Store, Silent = true };

            /// <summary>A failure ack with this status (and retryAfter, when given); nothing stored.</summary>
            public static TableAnswer WithStatus(int status, int? retryAfter = null) => new TableAnswer { Kind = TableAnswerKind.Status, Status = status, RetryAfter = retryAfter };

            /// <summary>This text as the answer; "{id}" is the frame's request id.</summary>
            public static TableAnswer WithText(string text) => new TableAnswer { Kind = TableAnswerKind.Text, Text = text };

            /// <summary>No answer, nothing stored.</summary>
            public static TableAnswer NoAnswer() => new TableAnswer { Kind = TableAnswerKind.Silent };

            /// <summary>The connection is closed with this code instead of an answer; nothing stored.</summary>
            public static TableAnswer CloseWith(int code, string reason = "") => new TableAnswer { Kind = TableAnswerKind.Close, Status = code, Text = reason };

            /// <summary>A binary frame instead of an answer (a protocol error of the server).</summary>
            public static TableAnswer BinaryFrame() => new TableAnswer { Kind = TableAnswerKind.Binary };

            /// <summary><c>allRefused</c>: every table of the block refused with <paramref name="reason"/>; nothing stored.</summary>
            public static TableAnswer RefuseAll(string reason = "SensorCreatedAfterBlock") => new TableAnswer { Kind = TableAnswerKind.RefuseAll, Text = reason };

            /// <summary>
            /// No answer: the session is cut without a close handshake instead, and the server closes the table
            /// connection with 1000 "its session ended". Nothing stored.
            /// </summary>
            public static TableAnswer DropSession() => new TableAnswer { Kind = TableAnswerKind.DropSession };
        }

        /// <summary>One block frame as the server read it.</summary>
        public sealed class TableFrame
        {
            public TableFrame(int connection, long id, Guid producer, long sequence, byte[] block)
            {
                Connection = connection;
                Id = id;
                Producer = producer;
                Sequence = sequence;
                Block = block;

                try
                {
                    Decoded = TableBlockReader.Read(block);
                }
                catch (Exception ex)
                {
                    DecodeError = ex.Message;
                }
            }

            /// <summary>Which table connection carried it: 1 for the first accepted.</summary>
            public int Connection { get; }

            public long Id { get; }

            public Guid Producer { get; }

            public long Sequence { get; }

            public byte[] Block { get; }

            public TableBlock Decoded { get; }

            public string DecodeError { get; }

            /// <summary>Frame length on the wire: header and block.</summary>
            public int FrameBytes => Block.Length + 40;

            public string Outcome { get; internal set; }

            public bool Stored { get; internal set; }

            internal Dictionary<string, string> Refused { get; set; }

            public IReadOnlyList<string> TableNames => Decoded?.Tables.Select(t => Encoding.UTF8.GetString(t.TableId)).ToList() ?? new List<string>();

            public IEnumerable<(long Ticks, double?[] Values)> RowsOf(string path)
            {
                var index = TableNames.ToList().IndexOf(path);

                if (index < 0 || (Refused != null && Refused.ContainsKey(path)))
                    yield break;

                var table = Decoded.Tables[index];

                for (var row = 0; row < table.RowCount; row++)
                {
                    var values = new double?[table.Columns.Count - 1];

                    for (var column = 1; column < table.Columns.Count; column++)
                    {
                        var data = table.Columns[column];

                        values[column - 1] = data.IsNull(row) ? (double?)null
                            : data.Type == TableColumnType.Double ? data.DoubleValues[row]
                            : data.Int64Values[row];
                    }

                    yield return (table.Time[row], values);
                }
            }
        }

        private sealed class TableSocket
        {
            private readonly SemaphoreSlim _send = new SemaphoreSlim(1, 1);

            internal TableSocket(WebSocket socket, int index, string sessionId)
            {
                Socket = socket;
                Index = index;
                SessionId = sessionId;
            }

            internal WebSocket Socket { get; }

            internal int Index { get; }

            internal string SessionId { get; }

            internal Task SendTextAsync(string text) => SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text);

            internal Task SendBinaryAsync(byte[] data) => SendAsync(data, WebSocketMessageType.Binary);

            private async Task SendAsync(byte[] data, WebSocketMessageType type)
            {
                await _send.WaitAsync().ConfigureAwait(false);

                try
                {
                    await Socket.SendAsync(new ArraySegment<byte>(data), type, true, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _send.Release();
                }
            }

            internal async Task CloseAsync(int code, string reason)
            {
                await _send.WaitAsync().ConfigureAwait(false);

                try
                {
                    if (Socket.State == WebSocketState.Open)
                        await Socket.CloseOutputAsync((WebSocketCloseStatus)code, reason, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
                finally
                {
                    _send.Release();
                }
            }
        }
    }
}
