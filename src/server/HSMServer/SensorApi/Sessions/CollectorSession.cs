using HSMServer.Core.Model;
using HSMServer.Middleware;
using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions
{
    // What the registry tells about one open session. A snapshot: the values do not change
    // after it is taken.
    public sealed record CollectorSessionInfo(
        Guid Id,
        Guid ProductId,
        string ProductName,
        Guid KeyId,
        string KeyName,
        string ClientName,
        string CollectorKind,
        string CollectorVersion,
        string AgentVersion,
        string RemoteIP,
        DateTime ConnectedAtUtc,
        DateTime LastFrameAtUtc);


    internal enum SessionFrameKind
    {
        Text,
        Binary,
        Close,
        TooBig,
    }


    // One inbound message. Dispose returns the buffer it may have rented.
    internal readonly struct SessionFrame(SessionFrameKind kind, byte[] buffer, int length, byte[] rented) : IDisposable
    {
        public SessionFrameKind Kind { get; } = kind;

        public int Length { get; } = length;

        public ReadOnlyMemory<byte> Payload => buffer.AsMemory(0, Length);


        public void Dispose()
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }


    // One collector's WebSocket and who is on the other end of it. The receive side belongs
    // to the handler loop alone; sends may come from any thread (acks from the loop, a
    // close at shutdown, pushes later) and are serialized here.
    internal sealed class CollectorSession : IDisposable
    {
        // Most frames are small. A larger one borrows from the pool for as long as it is
        // being handled, so an idle session holds only this.
        private const int IdleBufferBytes = 4 * 1024;

        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly byte[] _receiveBuffer = new byte[IdleBufferBytes];
        private readonly WebSocket _socket;

        private volatile PublicApiRequestInfo _caller;
        private string _callerProductName;
        private string _callerKeyName;

        private long _lastFrameTicks;
        private volatile string _endReason;

        // Guards the three fields below: which of the two, the handler loop or a close
        // asked for from outside, gets to the socket first.
        private readonly object _gate = new();
        private bool _handling;
        private bool _closing;
        private (WebSocketCloseStatus Status, string Reason)? _closeRequest;


        public Guid Id { get; } = Guid.NewGuid();

        public Guid KeyId { get; }

        public string RemoteIP { get; }

        public string ClientName { get; }

        public string CollectorKind { get; }

        public string CollectorVersion { get; }

        public string AgentVersion { get; }

        public DateTime ConnectedAtUtc { get; }

        // Who the session acts for, as last resolved from the cache. Replaced by the
        // handler loop when the key or the product changed; read by the registry.
        public PublicApiRequestInfo Caller => _caller;

        // Why the session ended, when the server ended it without a close handshake.
        public string EndReason => _endReason;


        public CollectorSession(WebSocket socket, PublicApiRequestInfo caller, string collectorKind, string collectorVersion, string agentVersion, DateTime utcNow)
        {
            _socket = socket;

            SetCaller(caller);
            KeyId = caller.Key.Id;
            RemoteIP = caller.RemoteIP;
            ClientName = caller.CollectorName;
            CollectorKind = collectorKind;
            CollectorVersion = collectorVersion;
            AgentVersion = agentVersion;
            ConnectedAtUtc = utcNow;

            _lastFrameTicks = utcNow.Ticks;
        }


        public void SetCaller(PublicApiRequestInfo caller)
        {
            _callerProductName = caller.Product.DisplayName;
            _callerKeyName = caller.Key.DisplayName;
            _caller = caller;
        }

        // The caller holds the product and key objects and a telemetry path built from
        // their names. It is stale when the cache hands out other objects, or when one of
        // the names was changed in place.
        public bool IsCallerCurrent(AccessKeyModel key, ProductModel product)
        {
            var caller = _caller;

            return ReferenceEquals(caller.Key, key) && ReferenceEquals(caller.Product, product) &&
                   _callerProductName == product.DisplayName && _callerKeyName == key.DisplayName;
        }

        public void Touch(DateTime utcNow) => Volatile.Write(ref _lastFrameTicks, utcNow.Ticks);

        public CollectorSessionInfo ToInfo()
        {
            var caller = Caller;

            return new CollectorSessionInfo(Id, caller.Product.Id, caller.Product.DisplayName, KeyId, caller.Key.DisplayName, ClientName,
                CollectorKind, CollectorVersion, AgentVersion, RemoteIP, ConnectedAtUtc, new DateTime(Volatile.Read(ref _lastFrameTicks), DateTimeKind.Utc));
        }


        // The handler loop is about to act on a frame. False when a close asked for from
        // outside is already on its way: the frame must then be left alone, neither stored
        // nor answered, so the collector sends it again elsewhere and nothing is doubled.
        public bool TryBeginFrame()
        {
            lock (_gate)
            {
                if (_closing)
                    return false;

                _handling = true;
                return true;
            }
        }

        // The frame is done and its answer is out. Returns the close that was asked for
        // meanwhile, for the loop to carry out now.
        public (WebSocketCloseStatus Status, string Reason)? EndFrame()
        {
            lock (_gate)
            {
                _handling = false;

                var request = _closeRequest;
                _closeRequest = null;

                return request;
            }
        }

        // Reads one whole message. Only the handler loop calls it. `progressed` is called
        // after every read from the socket, so the caller can tell a frame that is still
        // arriving from a collector that went silent.
        public async ValueTask<SessionFrame> ReceiveAsync(int maxFrameBytes, Action progressed, CancellationToken token)
        {
            var buffer = _receiveBuffer;
            byte[] rented = null;
            var total = 0;

            try
            {
                while (true)
                {
                    var result = await _socket.ReceiveAsync(buffer.AsMemory(total), token);

                    progressed?.Invoke();

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Return(ref rented);
                        return new SessionFrame(SessionFrameKind.Close, buffer, 0, null);
                    }

                    total += result.Count;

                    // Checked as the bytes arrive: a frame over the limit is never buffered
                    // beyond one byte past it.
                    if (total > maxFrameBytes)
                    {
                        Return(ref rented);
                        return new SessionFrame(SessionFrameKind.TooBig, buffer, 0, null);
                    }

                    if (result.EndOfMessage)
                    {
                        var kind = result.MessageType == WebSocketMessageType.Text ? SessionFrameKind.Text : SessionFrameKind.Binary;
                        return new SessionFrame(kind, buffer, total, rented);
                    }

                    if (total == buffer.Length)
                    {
                        // One byte past the limit is enough room to see a frame go over it.
                        var next = ArrayPool<byte>.Shared.Rent((int)Math.Min(buffer.Length * 2L, maxFrameBytes + 1L));

                        buffer.AsSpan(0, total).CopyTo(next);
                        Return(ref rented);

                        rented = next;
                        buffer = next;
                    }
                }
            }
            catch
            {
                Return(ref rented);
                throw;
            }
        }

        public async Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
        {
            await _sendLock.WaitAsync(token);

            try
            {
                await _socket.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, token);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        // The whole close handshake, from the handler loop (no receive is pending there):
        // our close frame, then the peer's. Bounded; a peer that does not answer is cut off.
        public async Task CloseAsync(WebSocketCloseStatus status, string reason)
        {
            using var timeout = new CancellationTokenSource(CloseTimeout);

            try
            {
                await _sendLock.WaitAsync(timeout.Token);

                try
                {
                    if (_socket.State == WebSocketState.Open)
                        await _socket.CloseAsync(status, reason, timeout.Token);
                    else if (_socket.State == WebSocketState.CloseReceived)
                        await _socket.CloseOutputAsync(status, reason, timeout.Token);
                }
                finally
                {
                    _sendLock.Release();
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                _socket.Abort();
            }
        }

        // A close asked for from outside the handler loop (shutdown). While the loop waits
        // in a receive, our half of the close goes out at once and the loop ends when it
        // reads the peer's. While the loop is busy with a frame, the close waits for it: the
        // frame's answer goes out first, so a batch that was stored is also acknowledged.
        public async Task SignalCloseAsync(WebSocketCloseStatus status, string reason, CancellationToken token)
        {
            lock (_gate)
            {
                if (_handling)
                {
                    _closeRequest = (status, reason);
                    return;
                }

                _closing = true;
            }

            try
            {
                await _sendLock.WaitAsync(token);

                try
                {
                    if (_socket.State == WebSocketState.Open)
                        await _socket.CloseOutputAsync(status, reason, token);
                }
                finally
                {
                    _sendLock.Release();
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                Abort(reason);
            }
        }

        // Ends the session without a handshake; the loop's pending receive fails.
        public void Abort(string reason)
        {
            _endReason ??= reason;
            _socket.Abort();
        }

        public bool IsOpen => _socket.State == WebSocketState.Open;

        public void Dispose()
        {
            _socket.Dispose();
            _sendLock.Dispose();
        }


        private static void Return(ref byte[] rented)
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
                rented = null;
            }
        }
    }
}
