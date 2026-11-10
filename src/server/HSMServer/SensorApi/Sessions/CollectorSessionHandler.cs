using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Middleware;
using HSMServer.Middleware.Telemetry;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using NLog;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions
{
    // The server side of a collector session (#1664): GET /api/sensors/session upgraded to
    // a WebSocket. A thin loop: read a frame, hand its body to the same ingestion service
    // the HTTP routes use, write the ack.
    //
    // Refusals before the upgrade are ordinary HTTP statuses. After it, the session ends
    // by a close handshake (either side), by the connection going away, or by silence; none
    // of these is an error of the server and none is logged as one.
    public sealed class CollectorSessionHandler
    {
        private const string MalformedBody = "malformed body";
        private const string EmptyBatch = "empty batch";
        private const string UnknownOp = "unknown op";
        private const string StoreFailed = "failed to store the batch";
        private const string CommandsFailed = "failed to apply the commands";

        private const string ShutdownReason = "server is shutting down";
        private const string SilenceReason = "no progress within three heartbeats";

        // How long shutdown waits for collectors to answer the close before cutting them
        // off. The web server's own shutdown would otherwise wait for these connections.
        private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(3);

        // A frame that is still arriving keeps the silence limit away, but not for ever: this
        // many silence limits after its first byte it has to be complete. Without the bound
        // a collector could hold a session, and its slot, with a frame that never ends, and
        // would never reach the access check that every completed frame gets.
        private const int FrameBudgetInSilenceLimits = 10;

        // An ack buffer that grew past this (a batch with many per-path errors) is dropped
        // after the frame, so one large answer does not stay allocated for the session.
        private const int KeepOutputBytes = 64 * 1024;

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly CollectorSessionsConfig _config;
        private readonly CollectorSessionRegistry _registry;
        private readonly SensorIngestionService _ingestion;
        private readonly ISensorApiTelemetry _telemetry;
        private readonly ITreeValuesCache _cache;
        private readonly TimeProvider _time;

        private volatile bool _stopping;
        private long _capWarnedAtTicks;


        public CollectorSessionHandler(IServerConfig config, CollectorSessionRegistry registry, SensorIngestionService ingestion,
            ISensorApiTelemetry telemetry, ITreeValuesCache cache, IHostApplicationLifetime lifetime, TimeProvider time)
        {
            _config = config.CollectorSessions ?? new CollectorSessionsConfig();
            _registry = registry;
            _ingestion = ingestion;
            _telemetry = telemetry;
            _cache = cache;
            _time = time;

            // ApplicationStopping fires before the web server starts waiting for its
            // connections, which is the moment to let the collectors go.
            lifetime.ApplicationStopping.Register(CloseAllForShutdown);
        }


        public async Task HandleAsync(HttpContext context)
        {
            if (!_config.Enabled)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                await Refuse(context, StatusCodes.Status400BadRequest, "A WebSocket upgrade is expected");
                return;
            }

            if (!context.TryGetPublicApiInfo(out var caller))
            {
                await context.SetAccessError("An access key is required");
                return;
            }

            if (!caller.Key.IsValid(KeyPermissions.CanSendSensorData, out var keyError))
            {
                await context.SetAccessError(keyError);
                return;
            }

            if (!context.WebSockets.WebSocketRequestedProtocols.Contains(SessionProtocol.Subprotocol))
            {
                await Refuse(context, StatusCodes.Status400BadRequest, $"The subprotocol {SessionProtocol.Subprotocol} is required");
                return;
            }

            if (_stopping)
            {
                await Refuse(context, StatusCodes.Status503ServiceUnavailable, ShutdownReason);
                return;
            }

            if (!_registry.TryReserve(_config.MaxSessions))
            {
                WarnCapReached(caller);

                await Refuse(context, StatusCodes.Status503ServiceUnavailable, "The session limit of this server is reached");
                return;
            }

            CollectorSession session = null;
            WebSocket socket = null;

            try
            {
                socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                {
                    SubProtocol = SessionProtocol.Subprotocol,
                    // Protocol-level keep-alive, for NAT and proxy idle timers. Liveness is
                    // decided by the application heartbeat, not by this.
                    KeepAliveInterval = _config.Heartbeat,
                });

                session = new CollectorSession(socket, caller,
                    ReadFact(context, SessionProtocol.CollectorKindHeader),
                    ReadFact(context, SessionProtocol.CollectorVersionHeader),
                    ReadFact(context, SessionProtocol.AgentVersionHeader),
                    _time.GetUtcNow().UtcDateTime);

                _registry.Register(session);
                SessionProtocol.MarkUpgraded(context);

                _logger.Debug($"Collector session {session.Id} opened: {caller.TelemetryPath} from {caller.RemoteIP}");

                // The shutdown sweep may have run between the reservation and the registration.
                if (_stopping)
                    await session.CloseAsync(SessionProtocol.CloseGoingAway, ShutdownReason);
                else
                    await RunAsync(session, context.RequestAborted);
            }
            catch (Exception ex) when (IsConnectionEnd(ex))
            {
                // The collector went away, or the server cut a silent session off.
            }
            catch (Exception ex)
            {
                _logger.Error($"Collector session {session?.Id} failed: {ex}");
                session?.Abort("internal error");
            }
            finally
            {
                _registry.Release(session);

                if (session is not null)
                {
                    _logger.Debug($"Collector session {session.Id} closed{(session.EndReason is null ? string.Empty : $": {session.EndReason}")}");
                    session.Dispose();
                }
                else if (socket is not null)
                {
                    // Accepted, but no session came to own it.
                    socket.Abort();
                    socket.Dispose();
                }
            }
        }


        private async Task RunAsync(CollectorSession session, CancellationToken aborted)
        {
            var output = new ArrayBufferWriter<byte>(256);

            // A collector that neither sends data nor pings for three heartbeats is gone
            // without a close: a dead host, a cut network. The same limit bounds a send to a
            // collector that stopped reading. The watch ends the session, which fails
            // whatever the loop is waiting in. It does not run while the server itself is
            // busy with a frame: a slow store is not the collector's silence. And it starts
            // over with every read, so a large frame on a slow link is not silence either.
            await using var silence = new SilenceWatch(_time, session, _config.SilenceLimit);

            var frameBudget = _config.SilenceLimit * FrameBudgetInSilenceLimits;
            long frameStarted = 0;

            // Called after every read of the frame being received. Bytes that arrive start the
            // silence limit again, within the frame's budget; an empty fragment is not progress.
            Action<int> progressed = bytes =>
            {
                if (bytes == 0)
                    return;

                if (frameStarted == 0)
                    frameStarted = _time.GetTimestamp();

                if (_time.GetElapsedTime(frameStarted) < frameBudget)
                    silence.Arm();
            };

            SessionProtocol.WriteHello(output, _config.MaxFrameBytes, _config.HeartbeatSeconds);
            _telemetry.AddResponse(session.Caller.TelemetryPath, output.WrittenCount);

            silence.Arm();
            await session.SendAsync(output.WrittenMemory, aborted);

            while (true)
            {
                silence.Arm();
                frameStarted = 0;

                using var frame = await session.ReceiveAsync(_config.MaxFrameBytes, progressed, aborted);

                silence.Disarm();

                switch (frame.Kind)
                {
                    case SessionFrameKind.Close:
                        // The collector closed. Answer the handshake if we have not started one.
                        await session.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty);
                        return;

                    case SessionFrameKind.TooBig:
                        await Violation(session, SessionProtocol.CloseMessageTooBig, $"a frame exceeds {_config.MaxFrameBytes} bytes");
                        return;

                    case SessionFrameKind.Binary:
                        await Violation(session, SessionProtocol.CloseUnsupportedData, "binary frames are not supported");
                        return;
                }

                // The server is closing this session (shutdown): what arrives now is left
                // unstored and unanswered, and the loop only waits for the collector's close.
                if (!session.TryBeginFrame())
                    continue;

                bool open;
                (WebSocketCloseStatus Status, string Reason)? askedToClose;

                try
                {
                    session.Touch(_time.GetUtcNow().UtcDateTime);

                    open = await HandleFrameAsync(session, frame, output, silence, aborted);
                }
                finally
                {
                    askedToClose = session.EndFrame();
                }

                if (!open)
                    return;

                // Asked for while the frame was being handled, and held back until its
                // answer was out.
                if (askedToClose is { } close)
                {
                    await session.CloseAsync(close.Status, close.Reason);
                    return;
                }

                if (output.Capacity > KeepOutputBytes)
                    output = new ArrayBufferWriter<byte>(256);
            }
        }

        // False when the session was closed and the loop must end.
        private async Task<bool> HandleFrameAsync(CollectorSession session, SessionFrame frame, ArrayBufferWriter<byte> output, SilenceWatch silence, CancellationToken aborted)
        {
            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(frame.Payload);
            }
            catch (JsonException)
            {
                await Violation(session, SessionProtocol.CloseInvalidPayload, "a frame is not JSON");
                return false;
            }

            using (document)
            {
                if (!SessionRequest.TryRead(document, out var request))
                {
                    await Violation(session, SessionProtocol.CloseInvalidPayload, "a frame is not a JSON object");
                    return false;
                }

                var isPing = request.Id is null && request.Op == SessionProtocol.OpPing;

                if (request.Id is null && !isPing)
                {
                    if (request.Op == SessionProtocol.OpPong)
                        return true;

                    // Without an id there is nothing to acknowledge, so the collector could
                    // never learn what became of the frame. Refuse loudly instead of
                    // dropping data silently.
                    await Violation(session, SessionProtocol.CloseProtocolError, "a request frame needs an id");
                    return false;
                }

                // The key is looked up again for every request and every ping, as it is for
                // every HTTP request: revoking or blocking it ends the session, also one that
                // only keeps itself alive. A ping is not a use of the key, though.
                if (!TryRefreshCaller(session, countsAsKeyUsage: !isPing, out var caller, out var accessError))
                {
                    _logger.Debug($"Collector session {session.Id} lost its access: {accessError}");
                    await session.CloseAsync(SessionProtocol.ClosePolicyViolation, Truncate(accessError));
                    return false;
                }

                if (isPing)
                {
                    silence.Arm();
                    await session.SendAsync(SessionProtocol.Pong, aborted);
                    return true;
                }

                var id = request.Id.Value;

                _telemetry.AddRequest(caller.TelemetryPath, frame.Length);

                output.Clear();

                switch (request.Op)
                {
                    case SessionProtocol.OpList:
                        await HandleListAsync(caller, request, id, output);
                        break;

                    case SessionProtocol.OpCommands:
                        await HandleCommandsAsync(caller, request, id, output);
                        break;

                    default:
                        SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, UnknownOp);
                        break;
                }

                _telemetry.AddResponse(caller.TelemetryPath, output.WrittenCount);

                silence.Arm();
                await session.SendAsync(output.WrittenMemory, aborted);

                return true;
            }
        }

        private async Task HandleListAsync(PublicApiRequestInfo caller, SessionRequest request, long id, ArrayBufferWriter<byte> output)
        {
            if (!TryReadBody<List<SensorValueBase>>(request, SensorApiJson.Values, out var values))
            {
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, MalformedBody);
                return;
            }

            if (values.Count == 0)
            {
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, EmptyBatch);
                return;
            }

            try
            {
                var errors = await _ingestion.AddValuesAsync(caller, values);

                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusOk, errors);
            }
            catch (Exception ex)
            {
                // The HTTP route answers 400 here as well.
                _logger.Error($"Failed to put data of collector session frame {id}: {ex}");

                output.Clear();
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, StoreFailed);
            }
        }

        private async Task HandleCommandsAsync(PublicApiRequestInfo caller, SessionRequest request, long id, ArrayBufferWriter<byte> output)
        {
            if (!TryReadBody<List<CommandRequestBase>>(request, SensorApiJson.Commands, out var commands))
            {
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, MalformedBody);
                return;
            }

            try
            {
                var errors = new Dictionary<string, string>(commands.Count);

                foreach (var command in commands)
                {
                    var error = await _ingestion.ApplyCommandAsync(caller, command);

                    if (error is not null)
                        errors[command.Path] = error;
                }

                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusOk, errors);
            }
            catch (Exception ex)
            {
                // The HTTP route answers 400 here as well.
                _logger.Error($"Failed to update sensors of collector session frame {id}: {ex}");

                output.Clear();
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, CommandsFailed);
            }
        }

        // The body is read with the options of the HTTP model binder for the same route.
        private static bool TryReadBody<T>(SessionRequest request, JsonSerializerOptions options, out T body) where T : class
        {
            body = null;

            if (!request.HasBody)
                return false;

            try
            {
                body = request.Body.Deserialize<T>(options);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException)
            {
                return false;
            }

            return body is not null;
        }

        private bool TryRefreshCaller(CollectorSession session, bool countsAsKeyUsage, out PublicApiRequestInfo caller, out string error)
        {
            caller = session.Caller;

            if (!_cache.TryGetKey(session.KeyId, out var key, out error))
                return false;

            if (!_cache.TryGetRootProduct(key.ProductId, out var product, out error))
                return false;

            if (!key.IsValid(KeyPermissions.CanSendSensorData, out error))
                return false;

            if (countsAsKeyUsage)
                _cache.SetLastKeyUsage(key.Id, session.RemoteIP);

            // Rebuilt only when something it is made of changed.
            if (!session.IsCallerCurrent(key, product))
            {
                caller = new PublicApiRequestInfo
                {
                    Product = product,
                    Key = key,
                    RemoteIP = session.RemoteIP,
                    CollectorName = session.ClientName,
                }.Init();

                session.SetCaller(caller);
            }

            return true;
        }

        private async Task Violation(CollectorSession session, WebSocketCloseStatus status, string reason)
        {
            _logger.Warn($"Collector session {session.Id} ({session.Caller.TelemetryPath}) is closed with {(int)status}: {reason}");

            await session.CloseAsync(status, reason);
        }

        // The refused collector keeps working over HTTP, so nothing else would tell the
        // operator that the cap is too low. At most one line a minute: a fleet over the cap
        // retries.
        private void WarnCapReached(PublicApiRequestInfo caller)
        {
            var now = _time.GetUtcNow().UtcTicks;
            var last = Interlocked.Read(ref _capWarnedAtTicks);

            if (now - last < TimeSpan.TicksPerMinute || Interlocked.CompareExchange(ref _capWarnedAtTicks, now, last) != last)
                return;

            _logger.Warn($"The collector session limit is reached (CollectorSessions.MaxSessions = {_config.MaxSessions}): the upgrade of {caller.TelemetryPath} from {caller.RemoteIP} " +
                         "is refused with 503 and it stays on HTTP. Raise the limit if this server has more collectors than that.");
        }

        private void CloseAllForShutdown()
        {
            _stopping = true;

            try
            {
                // The lifetime callback is synchronous, so this waits; the budget bounds it.
                // On the thread pool, so that nothing in the sweep tries to come back to
                // the thread that is waiting here.
                Task.Run(() => _registry.CloseAllAsync(SessionProtocol.CloseGoingAway, ShutdownReason, ShutdownBudget)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to close collector sessions at shutdown: {ex}");
            }
        }


        private static Task Refuse(HttpContext context, int status, string reason)
        {
            context.Response.StatusCode = status;

            return context.Response.WriteAsync(reason);
        }

        private static string ReadFact(HttpContext context, string header) =>
            context.TryReadInfo(header, out var value) && !string.IsNullOrWhiteSpace(value) ? Truncate(value.Trim()) : null;

        // A close reason is at most 123 bytes on the wire; header facts are kept short too.
        private static string Truncate(string text) => text is null || text.Length <= 100 ? text : text[..100];

        private static bool IsConnectionEnd(Exception ex) =>
            ex is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException;


        // Ends a session that makes no progress for `limit` while the loop waits for the
        // collector: for its next frame, or for it to take what the server sends.
        private sealed class SilenceWatch(TimeProvider time, CollectorSession session, TimeSpan limit) : IAsyncDisposable
        {
            private readonly ITimer _timer = time.CreateTimer(_ => session.Abort(SilenceReason), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);


            public void Arm() => _timer.Change(limit, Timeout.InfiniteTimeSpan);

            public void Disarm() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            public ValueTask DisposeAsync() => _timer.DisposeAsync();
        }
    }
}
