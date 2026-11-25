using HSMDatabase.AccessManager.TableBlocks;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.TableSensors;
using HSMServer.Extensions;
using HSMServer.Middleware;
using HSMServer.Middleware.Telemetry;
using HSMServer.ServerConfiguration;
using HSMTableBlocks;
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
    //
    // What is logged: every session that opens (Info) and every session that ends (Info for
    // an orderly close by either side, Warn otherwise), both under the session's short id,
    // and every refused upgrade (at most one line a minute for each kind of refusal). The
    // lines carry the key's name, never its value: the value is the key.
    //
    // The table connection (#1845, ADR-0021 point 7) runs through the same loop: a second
    // WebSocket of a collector, subprotocol hsm.tables.v1, tied to a live ordinary session of the
    // same key (X-Hsm-Session), taking no slot, at most one per session (a new one replaces the
    // old) and closed when its session ends. Its requests are binary frames (TableFrame), each
    // answered with a text ack: a block put into the block store, or a probe. One block in
    // flight per connection: the collector sends the next after the ack (stop and wait).
    public sealed class CollectorSessionHandler
    {
        private const string MalformedBody = "malformed body";
        private const string EmptyBatch = "empty batch";
        private const string UnknownOp = "unknown op";
        private const string StoreFailed = "failed to store the batch";
        private const string CommandsFailed = "failed to apply the commands";
        private const string BlockStoreUnavailable = "the block store cannot take blocks now";
        private const string BlockFailed = "failed to store the block";

        private const string TableReplacedReason = "replaced by a new table connection of its session";
        private const string BudgetTimeoutReason = "the server holds too many table frames; try again later";

        // 1013 Try Again Later (RFC 6455 registry): the end of a wait for the table frame budget.
        private const WebSocketCloseStatus CloseTryAgainLater = (WebSocketCloseStatus)1013;

        // How long a table connection waits for the frame budget before it gives up. A reservation
        // is held for the time of one frame and its put (milliseconds when the store is well; a
        // put that faults is answered at once), so a wait this long means puts are not coming
        // back: twice the silence limit (3 minutes by default), past the block store's own
        // one-minute backoff.
        private TimeSpan BudgetWaitBound => _config.SilenceLimit * 2;
        private const string SessionEndedReason = "its session ended";

        private static readonly Dictionary<string, string> NoErrors = new();

        private const string ShutdownReason = "server is shutting down";
        private const string SilenceReason = "no progress within three heartbeats";

        // How long shutdown waits for collectors to answer the close before cutting them
        // off. The web server's own shutdown would otherwise wait for these connections.
        private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(3);

        // Sent as Retry-After with the refusals that a wait can cure (a full server, a full
        // address).
        private const int RetryAfterSeconds = 60;

        // A frame that is still arriving keeps the silence limit away, but not for ever: this
        // many silence limits after its first byte it has to be complete. Without the bound
        // a collector could hold a session, and its slot, with a frame that never ends, and
        // would never reach the access check that every completed frame gets.
        private const int FrameBudgetInSilenceLimits = 10;

        // A frame of a table connection that holds a reservation of the frame budget (#1845) must
        // arrive whole within this many silence limits from the moment it was reserved, however its
        // bytes trickle in: past it the connection is cut and the reservation goes back. One limit
        // (90 s by default) is enough for the largest frame (4 MiB by default, about 47 KB/s), and a
        // reservation is what every other collector's large frame waits for; the ten limits an
        // ordinary frame may take would let a slow sender hold the budget for a quarter of an hour.
        private const int ReservedFrameDeadlineInSilenceLimits = 1;

        private const string ReservedFrameTooSlowReason = "a large table frame took too long to arrive";

        // An ack buffer that grew past this (a batch with many per-path errors) is dropped
        // after the frame, so one large answer does not stay allocated for the session.
        private const int KeepOutputBytes = 64 * 1024;

        private Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly CollectorSessionsConfig _config;
        private readonly CollectorSessionRegistry _registry;
        private readonly SensorIngestionService _ingestion;
        private readonly ISensorApiTelemetry _telemetry;
        private readonly ITreeValuesCache _cache;
        private readonly AgentDirectives _directives;
        private readonly TimeProvider _time;
        private readonly ITableBlockService _tables;
        private readonly TableBlockLimits _tableLimits;

        // Blocks stored at once by all table connections (TableSensors.MaxConcurrentPuts).
        private readonly SemaphoreSlim _putGate;

        // What table connections hold for frames (CollectorSessions.MaxTableBytesInFlight).
        private readonly TableFrameBudget _tableBudget;

        private volatile bool _stopping;
        private long _capWarnedAtTicks;
        private long _offWarnedAtTicks;
        private long _tablesOffWarnedAtTicks;
        private long _budgetWarnedAtTicks;
        private long _ownKeyWarnedAtTicks;

        // Refused upgrades, one throttle per kind of refusal.
        private readonly RefusalThrottle _badUpgradeRefusals = new();
        private readonly RefusalThrottle _accessRefusals = new();
        private readonly RefusalThrottle _shutdownRefusals = new();
        private readonly RefusalThrottle _tableRefusals = new();


        public CollectorSessionHandler(IServerConfig config, CollectorSessionRegistry registry, SensorIngestionService ingestion,
            ISensorApiTelemetry telemetry, ITreeValuesCache cache, AgentDirectives directives, IHostApplicationLifetime lifetime, TimeProvider time,
            ITableBlockService tables)
        {
            _config = config.CollectorSessions ?? new CollectorSessionsConfig();
            _tables = tables;

            var tableConfig = config.TableSensors ?? new TableSensorsConfig();
            _tableLimits = tableConfig.ToOptions().Limits;
            _putGate = new SemaphoreSlim(tableConfig.MaxConcurrentPuts, tableConfig.MaxConcurrentPuts);
            _tableBudget = new TableFrameBudget(_config.MaxTableBytesInFlight);
            _registry = registry;
            _ingestion = ingestion;
            _telemetry = telemetry;
            _cache = cache;
            _directives = directives;
            _time = time;

            // ApplicationStopping fires before the web server starts waiting for its
            // connections, which is the moment to let the collectors go.
            lifetime.ApplicationStopping.Register(CloseAllForShutdown);
        }


        public async Task HandleAsync(HttpContext context)
        {
            if (!_config.Enabled)
            {
                // A collector that is set to use a session has no other way to deliver, so
                // an upgrade arriving here means values are piling up somewhere.
                if (context.WebSockets.IsWebSocketRequest && IsTimeToWarn(ref _offWarnedAtTicks))
                    _logger.Warn($"A collector asked for a session from {context.Connection.RemoteIpAddress}, but collector sessions are switched off on this server " +
                                 "(CollectorSessions.MaxSessions = 0): it is refused with 404 and cannot send. Turn sessions on, or set that collector to HTTP.");

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                LogRefusal(context, _badUpgradeRefusals, StatusCodes.Status400BadRequest, "not a WebSocket upgrade", LogLevel.Warn);
                await Refuse(context, StatusCodes.Status400BadRequest, "A WebSocket upgrade is expected");
                return;
            }

            if (!context.TryGetPublicApiInfo(out var caller))
            {
                // No usable key: missing, malformed or unknown. Which one is not known here,
                // and the value is not logged in any case.
                LogRefusal(context, _accessRefusals, StatusCodes.Status406NotAcceptable, "no valid access key", LogLevel.Warn);
                await context.SetAccessError("An access key is required");
                return;
            }

            if (!caller.Key.IsValid(KeyPermissions.CanSendSensorData, out var keyError))
            {
                LogRefusal(context, _accessRefusals, StatusCodes.Status406NotAcceptable, $"the key \"{CollectorSession.Printable(caller.Key.DisplayName)}\" cannot send data: {keyError?.TrimEnd('.')}", LogLevel.Warn);
                await context.SetAccessError(keyError);
                return;
            }

            // A table connection names its own subprotocol (#1845); a server before it refuses
            // that upgrade here, with the 400 below, before anything is upgraded.
            var requested = context.WebSockets.WebSocketRequestedProtocols;
            var isTable = requested.Contains(SessionProtocol.TablesSubprotocol);

            if (!isTable && !requested.Contains(SessionProtocol.Subprotocol))
            {
                LogRefusal(context, _badUpgradeRefusals, StatusCodes.Status400BadRequest, $"the subprotocol {SessionProtocol.Subprotocol} was not offered", LogLevel.Warn);
                await Refuse(context, StatusCodes.Status400BadRequest, $"The subprotocol {SessionProtocol.Subprotocol} is required");
                return;
            }

            if (isTable && !_config.TablesEnabled)
            {
                // Switched off on purpose (CollectorSessions.MaxTableFrameBytes = 0): that
                // collector keeps its blocks and cannot deliver them.
                if (IsTimeToWarn(ref _tablesOffWarnedAtTicks))
                    _logger.Warn($"A collector asked for a table connection from {context.Connection.RemoteIpAddress}, but table connections are switched off on this server " +
                                 "(CollectorSessions.MaxTableFrameBytes = 0): it is refused with 404 and cannot deliver table blocks.");

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (_stopping)
            {
                LogRefusal(context, _shutdownRefusals, StatusCodes.Status503ServiceUnavailable, ShutdownReason, LogLevel.Info);
                await Refuse(context, StatusCodes.Status503ServiceUnavailable, ShutdownReason);
                return;
            }

            if (isTable)
            {
                await HandleTableUpgradeAsync(context, caller);
                return;
            }

            switch (_registry.TryReserve(caller.RemoteIP, _config.MaxSessions, _config.MaxSessionsPerAddress, _time.GetUtcNow().UtcTicks, out var worthLogging))
            {
                case SlotRefusal.AddressFull:
                    if (worthLogging)
                        WarnAddressLimitReached(caller);

                    await Refuse(context, StatusCodes.Status429TooManyRequests, "The session limit for this address is reached", RetryAfterSeconds);
                    return;

                case SlotRefusal.ServerFull:
                    WarnCapReached(caller);

                    await Refuse(context, StatusCodes.Status503ServiceUnavailable, "The session limit of this server is reached", RetryAfterSeconds);
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

                LogOpened(session);

                // The shutdown sweep may have run between the reservation and the registration.
                if (_stopping)
                    await session.CloseAsync(SessionProtocol.CloseGoingAway, ShutdownReason);
                else
                    await RunAsync(session, context.RequestAborted);
            }
            catch (Exception ex) when (IsConnectionEnd(ex))
            {
                // The collector went away, or the server cut a silent session off. In the
                // second case the cause is noted already, and this note does not replace it.
                session?.NoteEnd($"the connection ended without a close handshake ({ex.GetType().Name}: {ex.Message})", abnormal: true);
            }
            catch (Exception ex)
            {
                _logger.Error($"Collector session {session?.ShortId} failed: {ex}");
                session?.Abort($"internal error ({ex.GetType().Name})");
            }
            finally
            {
                var table = _registry.Release(session, caller.RemoteIP);

                if (session is not null)
                {
                    try
                    {
                        LogClosed(session);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to log the end of collector session {session.ShortId}: {ex}");
                    }

                    session.Dispose();
                }
                else if (socket is not null)
                {
                    // Accepted, but no session came to own it.
                    socket.Abort();
                    socket.Dispose();
                }

                // A table connection lives as long as its session: closed now, after the ack of a
                // block it is storing.
                if (table is not null)
                    await CloseTableAsync(table, SessionEndedReason);
            }
        }

        // A table connection (#1845): checked against the ordinary session it names, accepted,
        // attached to it in place of an earlier one, and run through the same loop.
        private async Task HandleTableUpgradeAsync(HttpContext context, PublicApiRequestInfo caller)
        {
            var named = ReadFact(context, SessionProtocol.SessionHeader);

            if (named is null || !SessionProtocol.TryParseSessionId(named, out var sessionId) ||
                _registry.FindSession(sessionId) is not { } owner || owner.KeyId != caller.Key.Id)
            {
                LogRefusal(context, _tableRefusals, StatusCodes.Status409Conflict,
                    named is null ? $"no {SessionProtocol.SessionHeader}" : $"{SessionProtocol.SessionHeader} names no live session of this key", LogLevel.Warn);
                await Refuse(context, StatusCodes.Status409Conflict,
                    $"A table connection needs the id of a live session of the same key in {SessionProtocol.SessionHeader}: open the session first");
                return;
            }

            CollectorSession table = null;
            WebSocket socket = null;

            try
            {
                socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
                {
                    SubProtocol = SessionProtocol.TablesSubprotocol,
                    KeepAliveInterval = _config.Heartbeat,
                });

                table = new CollectorSession(socket, caller,
                    ReadFact(context, SessionProtocol.CollectorKindHeader),
                    ReadFact(context, SessionProtocol.CollectorVersionHeader),
                    agentVersion: null,
                    _time.GetUtcNow().UtcDateTime,
                    pairedSessionId: sessionId);

                SessionProtocol.MarkUpgraded(context);

                if (!_registry.TryAttachTable(table, out var replaced))
                {
                    // The session ended between the check and here.
                    LogOpened(table);
                    await table.CloseAsync(WebSocketCloseStatus.NormalClosure, SessionEndedReason);
                    return;
                }

                LogOpened(table);

                if (replaced is not null)
                    await CloseTableAsync(replaced, TableReplacedReason);

                if (_stopping)
                    await table.CloseAsync(SessionProtocol.CloseGoingAway, ShutdownReason);
                else
                    await RunAsync(table, context.RequestAborted);
            }
            catch (Exception ex) when (IsConnectionEnd(ex))
            {
                table?.NoteEnd($"the connection ended without a close handshake ({ex.GetType().Name}: {ex.Message})", abnormal: true);
            }
            catch (Exception ex)
            {
                _logger.Error($"Collector table connection {table?.ShortId} failed: {ex}");
                table?.Abort($"internal error ({ex.GetType().Name})");
            }
            finally
            {
                if (table is not null)
                {
                    _registry.DetachTable(table);

                    try
                    {
                        LogClosed(table);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to log the end of collector table connection {table.ShortId}: {ex}");
                    }

                    table.Dispose();
                }
                else if (socket is not null)
                {
                    socket.Abort();
                    socket.Dispose();
                }
            }
        }

        // Closes a table connection from outside its loop: at once while it waits for a frame,
        // after the ack while it is storing a block (so a stored block is acknowledged and not
        // sent again). A peer that does not answer the close in time is cut off.
        private async Task CloseTableAsync(CollectorSession table, string reason)
        {
            using var budget = new CancellationTokenSource(ShutdownBudget);

            try
            {
                await table.SignalCloseAsync(WebSocketCloseStatus.NormalClosure, reason, budget.Token);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to close collector table connection {table.ShortId}: {ex}");
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

            var maxFrameBytes = session.IsTable ? _config.MaxTableFrameBytes : _config.MaxFrameBytes;

            // A table connection's large frames take from the server-wide budget (#1845); whatever
            // it holds goes back when the loop ends, on every path. A reserved frame has a deadline of
            // its own, which bytes arriving do not move.
            await using var frameDeadline = session.IsTable
                ? new SilenceWatch(_time, session, _config.SilenceLimit * ReservedFrameDeadlineInSilenceLimits, ReservedFrameTooSlowReason)
                : null;

            using var memory = session.IsTable
                ? new TableFrameMemory(_tableBudget, maxFrameBytes, session.KeyId, (reservation, token) => WaitForTableBudgetAsync(session, silence, reservation, token), frameDeadline.Arm)
                : null;

            if (session.IsTable)
                SessionProtocol.WriteTableHello(output, maxFrameBytes, _config.HeartbeatSeconds, _tableLimits);
            else
                SessionProtocol.WriteHello(output, maxFrameBytes, _config.HeartbeatSeconds, session.Id);

            _telemetry.AddResponse(session.Caller.TelemetryPath, output.WrittenCount);

            silence.Arm();
            await session.SendAsync(output.WrittenMemory, aborted);

            // Directives for a directive-aware agent (#1660 step 5): the rule of the header path,
            // a client that stated X-Agent-Version. After the hello, which must be the first frame.
            // A table connection states none.
            await using var push = session.AgentVersion is null || session.IsTable
                ? null
                : new DirectivePush(session, _directives, _cache, _telemetry, _logger, _time, (frame, token) => PushSend(session, frame, token));

            while (true)
            {
                silence.Arm();
                frameStarted = 0;

                SessionFrame received;

                try
                {
                    received = await session.ReceiveAsync(maxFrameBytes, progressed, aborted, memory);
                }
                catch (TableFrameBudgetTimeoutException)
                {
                    // Mid-frame, so there is no request id to answer 503 to: the connection is
                    // closed, the block it was sending is not stored, and the collector sends it
                    // again on its next table connection.
                    await session.CloseAsync(CloseTryAgainLater, BudgetTimeoutReason);
                    return;
                }

                using var frame = received;

                silence.Disarm();
                frameDeadline?.Disarm();

                // The watch may have fired just as the frame completed: the session is then
                // over, and acting on the frame would store a batch whose ack can no longer
                // be sent. Left alone, the collector sends it again and nothing is doubled.
                if (session.EndReason is not null)
                    return;

                switch (frame.Kind)
                {
                    case SessionFrameKind.Close:
                        // The collector closed. Answer the handshake if we have not started one.
                        session.NoteCollectorClose();
                        await session.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty);
                        return;

                    case SessionFrameKind.TooBig:
                        await Violation(session, SessionProtocol.CloseMessageTooBig, $"a frame exceeds {maxFrameBytes} bytes");
                        return;

                    // Binary frames carry blocks, and only on a table connection: the ordinary
                    // connection stays text-only.
                    case SessionFrameKind.Binary when !session.IsTable:
                        await Violation(session, SessionProtocol.CloseUnsupportedData, "binary frames are not supported");
                        return;
                }

                // The server is closing this session (shutdown): what arrives now is left
                // unstored and unanswered, and the loop only waits for the collector's close.
                if (!session.TryBeginFrame())
                {
                    memory?.EndFrame();
                    continue;
                }

                bool open;
                (WebSocketCloseStatus Status, string Reason)? askedToClose;

                try
                {
                    session.Touch(_time.GetUtcNow().UtcDateTime);

                    open = frame.Kind == SessionFrameKind.Binary
                        ? await HandleTableFrameAsync(session, frame, output, silence, aborted)
                        : await HandleFrameAsync(session, frame, output, silence, aborted);
                }
                finally
                {
                    askedToClose = session.EndFrame();

                    // The frame and its put are done: its reservation goes back.
                    memory?.EndFrame();
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

                // A heartbeat is known by its op. The contract gives it no id, but one that
                // carries an id is still a heartbeat, not an unknown request.
                var isPing = request.Op == SessionProtocol.OpPing;

                if (request.Op == SessionProtocol.OpPong)
                    return true;

                if (request.Id is null && !isPing)
                {

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
                    _logger.Debug($"Collector session {session.ShortId} lost its access: {accessError}");
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

                // A table connection carries blocks only: every text request is an unknown op there.
                switch (request.Op)
                {
                    case SessionProtocol.OpList when !session.IsTable:
                        await HandleListAsync(session, caller, request, id, output);
                        break;

                    case SessionProtocol.OpCommands when !session.IsTable:
                        await HandleCommandsAsync(session, caller, request, id, output);
                        break;

                    default:
                        session.CountRequestRefused();
                        SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, UnknownOp);
                        break;
                }

                _telemetry.AddResponse(caller.TelemetryPath, output.WrittenCount);

                silence.Arm();
                await session.SendAsync(output.WrittenMemory, aborted);

                return true;
            }
        }

        // A table connection's frame outgrew the free allowance and the budget is spent: wait for a
        // reservation. The connection does not read on meanwhile (back-pressure through TCP), and,
        // as while a frame is handled, the silence watch does not run: the wait is the server's,
        // not the collector's silence. It has a bound of its own, and ends when the server ends
        // the connection (shutdown, a replacement, the end of its session).
        private async Task WaitForTableBudgetAsync(CollectorSession session, SilenceWatch silence, long reservation, CancellationToken token)
        {
            // Said only when the bytes are spent: a second large frame of a key that holds one may wait
            // for that one alone.
            if (_tableBudget.InUse + reservation > _tableBudget.Capacity && IsTimeToWarn(ref _budgetWarnedAtTicks))
                _logger.Warn($"The table frame budget is spent (CollectorSessions.{nameof(CollectorSessionsConfig.MaxTableBytesInFlight)} = {_config.MaxTableBytesInFlight}, " +
                             $"{_tableBudget.InUse} bytes held, {_tableBudget.Waiting} connection(s) waiting): table connection {session.ShortId} from {session.RemoteIP} " +
                             "stops reading its frame until frames being stored are done. Raise the budget if this is steady.");

            silence.Disarm();

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token, session.Ending);
            var timedOut = 0;

            await using (_time.CreateTimer(_ => TimeOut(wait, ref timedOut), null, BudgetWaitBound, Timeout.InfiniteTimeSpan))
            {
                try
                {
                    await _tableBudget.TakeAsync(reservation, session.KeyId, wait.Token);
                }
                catch (OperationCanceledException) when (Volatile.Read(ref timedOut) == 1)
                {
                    // One reservation per access key: with the budget not spent, the wait was for
                    // another large frame of the same key (several collectors sharing a key).
                    if (_tableBudget.IsHeldBackOnlyByItsKey(reservation, session.KeyId) && IsTimeToWarn(ref _ownKeyWarnedAtTicks))
                        _logger.Warn($"Table connection {session.ShortId} from {session.RemoteIP} (client \"{CollectorSession.Printable(session.ClientName)}\") waited {BudgetWaitBound} " +
                                     "for a table frame reservation and is closed with 1013, held back only by its own access key: another connection of the same key " +
                                     "holds the key's one reservation for frames over 64 KiB, and the budget had room. Collectors sharing a key send such frames one at a time; " +
                                     "give them keys of their own if this is steady.");

                    throw new TableFrameBudgetTimeoutException();
                }
            }

            silence.Arm();
        }

        // For the tests, which read the budget.
        internal TableFrameBudget TableBudget => _tableBudget;

        // The bound of a budget wait has passed. A timer may fire as the wait ends and its source
        // is disposed: nothing is left to cancel then.
        private static void TimeOut(CancellationTokenSource wait, ref int timedOut)
        {
            Interlocked.Exchange(ref timedOut, 1);

            try
            {
                wait.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        // A binary frame of a table connection (#1845). False when the connection was closed and the
        // loop must end.
        private async Task<bool> HandleTableFrameAsync(CollectorSession session, SessionFrame frame, ArrayBufferWriter<byte> output, SilenceWatch silence, CancellationToken aborted)
        {
            // Without a header it can trust there is no request id to answer: a protocol error.
            if (!TableFrame.TryRead(frame.Payload.Span, out var header, out var error))
            {
                await Violation(session, SessionProtocol.CloseProtocolError, error);
                return false;
            }

            var isProbe = header.Kind == TableFrameKind.Probe;

            // Access is re-checked for every frame, as for a request on the ordinary connection; a
            // probe, like a ping, is not a use of the key.
            if (!TryRefreshCaller(session, countsAsKeyUsage: !isProbe, out var caller, out var accessError))
            {
                _logger.Debug($"Collector table connection {session.ShortId} lost its access: {accessError}");
                await session.CloseAsync(SessionProtocol.ClosePolicyViolation, Truncate(accessError));
                return false;
            }

            _telemetry.AddRequest(caller.TelemetryPath, frame.Length);

            output.Clear();

            if (isProbe)
                SessionProtocol.WriteAck(output, header.Id, SessionProtocol.StatusOk, NoErrors);
            else
                await HandleBlockAsync(session, caller, header, frame.Payload[header.BlockOffset..], output, aborted);

            _telemetry.AddResponse(caller.TelemetryPath, output.WrittenCount);

            silence.Arm();
            await session.SendAsync(output.WrittenMemory, aborted);

            return true;
        }

        // A block: put into the block store and answered by what came of it. A refusal (of tables,
        // of the whole block, of a block that cannot be read) is final and answered as one; a
        // fault of the store is a 503 with a time to wait, and the block was not stored, so the
        // collector keeps it and sends it again.
        private async Task HandleBlockAsync(CollectorSession session, PublicApiRequestInfo caller, TableFrame header, ReadOnlyMemory<byte> payload,
            ArrayBufferWriter<byte> output, CancellationToken aborted)
        {
            var id = header.Id;

            if (header.ProducerId == Guid.Empty || header.ProducerSequence < 0)
            {
                session.CountBlockInvalid();
                session.CountRequestRefused();
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest,
                    header.ProducerId == Guid.Empty ? "invalid producer: the producer id is empty" : "invalid producer: the producer sequence is negative");
                return;
            }

            var productId = caller.Product.Id;

            // The store takes a whole array; the frame's buffer is the session's and is reused.
            var block = payload.ToArray();

            TableBlockPutResult result;

            await _putGate.WaitAsync(aborted);

            try
            {
                // Off the loop's thread: a put decompresses the block and makes a synced write.
                result = await Task.Run(() => _tables.Put(productId, header.ProducerId, header.ProducerSequence, block), CancellationToken.None);
            }
            catch (TableBlockStoreException ex)
            {
                // The store logs its own failure; this is the collector's side of it.
                _logger.Debug($"Collector table connection {session.ShortId} frame {id}: the block store refused the put as a fault: {ex.Message}");

                session.CountBlockFault();
                session.CountRequestRefused();
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusUnavailable, BlockStoreUnavailable, SessionProtocol.StoreRetryAfterSeconds);
                return;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to store a block of collector table connection {session.ShortId} frame {id}: {ex}");

                session.CountBlockFault();
                session.CountRequestRefused();
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusInternalError, BlockFailed);
                return;
            }
            finally
            {
                _putGate.Release();
            }

            // Two collectors with one producer id make each other's blocks duplicates: the rows of
            // whichever is second are lost. Not refused (a restarted collector reconnects with its
            // own id while its old connection may still be listed), but said. Noted only for a block
            // the store took or knew: a frame that is not a block names no producer worth tracking.
            if (result.Outcome is TableBlockPutOutcome.Stored or TableBlockPutOutcome.Duplicate &&
                _registry.NoteProducer(session, productId, header.ProducerId, _time.GetUtcNow().UtcTicks, out var worthLogging, out var conflicts) is { } other && worthLogging)
                _logger.Warn($"Two live table connections of product \"{CollectorSession.Printable(caller.Product.DisplayName)}\" send blocks of one producer id {header.ProducerId}: " +
                             $"{session.ShortId} from {session.RemoteIP} (client \"{CollectorSession.Printable(session.ClientName)}\") and {other.ShortId} from {other.RemoteIP} " +
                             $"(client \"{CollectorSession.Printable(other.ClientName)}\"). Their blocks are taken for each other's repeats and rows are lost: " +
                             "each collector installation needs its own producer id (a copied data folder carries it along). " +
                             $"{conflicts} such block(s) on this server since the last line of this kind (one a minute).");

            switch (result.Outcome)
            {
                case TableBlockPutOutcome.Stored:
                    session.CountBlockStored(result.RefusedCount);
                    SessionProtocol.WriteBlockAck(output, id, "stored", result.TableCount, result.Refusals, result.RefusedCount);
                    break;

                case TableBlockPutOutcome.Duplicate:
                    session.CountBlockDuplicate();
                    SessionProtocol.WriteBlockAck(output, id, "duplicate", result.TableCount, result.RefusalsKnown ? result.Refusals : null, result.RefusedCount);
                    break;

                case TableBlockPutOutcome.AllRefused:
                    session.CountBlockAllRefused(result.RefusedCount);
                    SessionProtocol.WriteBlockAck(output, id, "allRefused", result.TableCount, result.Refusals, result.RefusedCount);
                    break;

                default:
                    session.CountBlockInvalid();
                    session.CountRequestRefused();
                    SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, $"invalid block: {result.FormatError}");
                    break;
            }
        }

        private async Task HandleListAsync(CollectorSession session, PublicApiRequestInfo caller, SessionRequest request, long id, ArrayBufferWriter<byte> output)
        {
            if (!TryReadBody<List<SensorValueBase>>(request, SensorApiJson.Values, out var values))
            {
                session.CountRequestRefused();
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, MalformedBody);
                return;
            }

            if (values.Count == 0)
            {
                session.CountRequestRefused();
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, EmptyBatch);
                return;
            }

            try
            {
                var errors = await _ingestion.AddValuesAsync(caller, values);

                // The errors are keyed by path: every value of a refused path is not accepted.
                // A value without a path is not counted (and must not throw here: the batch is stored, and a
                // statistic may not turn its ack into a refusal).
                session.CountValuesAccepted(errors is null || errors.Count == 0
                    ? values.Count
                    : values.Count(value => value?.Path is not null && !errors.ContainsKey(value.Path)));
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusOk, errors);
            }
            catch (Exception ex)
            {
                // The HTTP route answers 400 here as well.
                _logger.Error($"Failed to put data of collector session {session.ShortId} frame {id}: {ex}");

                session.CountRequestRefused();
                output.Clear();
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, StoreFailed);
            }
        }

        private async Task HandleCommandsAsync(CollectorSession session, PublicApiRequestInfo caller, SessionRequest request, long id, ArrayBufferWriter<byte> output)
        {
            // A list with a null element or a command without a path is a malformed body
            // too, as on the HTTP route (#1749): refused as a whole before anything is
            // applied.
            if (!TryReadBody<List<CommandRequestBase>>(request, SensorApiJson.Commands, out var commands) ||
                SensorApiJson.FindMalformedCommand(commands) is not null)
            {
                session.CountRequestRefused();
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusBadRequest, MalformedBody);
                return;
            }

            var errors = new Dictionary<string, string>(commands.Count);

            Exception firstFault = null;
            var faulted = 0;

            // The sensor moves of the whole frame first (#1724), as on the HTTP route.
            await _ingestion.ApplyPreviousPathsAsync(commands.Select(command => (caller, command)).ToList());

            foreach (var command in commands)
            {
                try
                {
                    var error = await _ingestion.ApplyCommandAsync(caller, command, previousPathsApplied: true);

                    if (error is not null)
                        errors[command.Path] = error;
                }
                catch (Exception ex)
                {
                    // As on the HTTP route (#1749): a fault on one command does not end the
                    // batch. The ack will be a 500, on which a collector sends the whole
                    // package again every cycle, ahead of everything it issues later, so a
                    // command that faults on every repeat must not keep the commands after
                    // it from being applied. They are applied now and once more on the
                    // resend, which is safe: a registration applied again is what every
                    // collector restart does.
                    firstFault ??= ex;
                    faulted++;
                }
            }

            if (firstFault is null)
            {
                SessionProtocol.WriteAck(output, id, SessionProtocol.StatusOk, errors);
                return;
            }

            // The HTTP route answers 500 here as well (#1749): the fault is the server's
            // own, and a collector takes a 4xx on a registration as a final refusal and
            // repeats a 5xx on its next cycle. One line for the frame, not one per command:
            // during a storage outage every command of a large package faults. And not one
            // per cycle either: the collector sends the frame again every cycle for as long
            // as the fault lasts, so the same fault of the same key is logged once per
            // window, with the number of requests that went unlogged since.
            if (_ingestion.IsFaultLogDue(caller.Key.Id, firstFault, out var unlogged))
                _logger.Error($"Failed to update sensors of collector session {session.ShortId} frame {id}: {faulted} of {commands.Count} command(s) faulted, the others were applied.{RepeatedFaultLog.Unlogged(unlogged)} The first fault: {firstFault}");

            // A failure ack has no body in version 1, so the refusals collected for the
            // other commands do not travel; a resend that does not fault carries them in
            // its 200.
            session.CountRequestRefused();
            SessionProtocol.WriteAck(output, id, SessionProtocol.StatusInternalError, CommandsFailed);
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
            catch (Exception)
            {
                // Whatever a converter throws on a body it cannot read: the hand-written ones
                // parse fields themselves and throw ArgumentException, OverflowException and
                // others besides JsonException. All of it is a malformed body, answered with
                // 400; none of it may end the session.
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

        // The close line of the session reports it, as a warning.
        private static Task Violation(CollectorSession session, WebSocketCloseStatus status, string reason) => session.CloseAsync(status, reason);

        // For the tests, which read what the handler logs without touching the process-wide
        // NLog configuration.
        internal void UseLogger(Logger logger) => _logger = logger;

        // How a directive push writes its frame: the session's serialized send. For the tests,
        // which make a push fail.
        internal Func<CollectorSession, ReadOnlyMemory<byte>, CancellationToken, Task> PushSend { get; set; } =
            (session, frame, token) => session.SendAsync(frame, token);

        private void LogOpened(CollectorSession session)
        {
            var caller = session.Caller;

            if (session.IsTable)
            {
                _logger.Info($"Collector table connection {session.ShortId} of session {session.PairedShortId} opened: product \"{CollectorSession.Printable(caller.Product.DisplayName)}\", " +
                             $"key \"{CollectorSession.Printable(caller.Key.DisplayName)}\", client \"{CollectorSession.Printable(session.ClientName)}\", " +
                             $"collector {CollectorSession.Printable(session.CollectorKind) ?? "(kind not stated)"} {CollectorSession.Printable(session.CollectorVersion) ?? "(version not stated)"}, " +
                             $"from {session.RemoteIP}; {_registry.TableConnectionCount} table connection(s) open.");
                return;
            }

            // The client's words (its name, what it says it is) are made printable first.
            _logger.Info($"Collector session {session.ShortId} opened: product \"{CollectorSession.Printable(caller.Product.DisplayName)}\", " +
                         $"key \"{CollectorSession.Printable(caller.Key.DisplayName)}\", client \"{CollectorSession.Printable(session.ClientName)}\", " +
                         $"collector {CollectorSession.Printable(session.CollectorKind) ?? "(kind not stated)"} {CollectorSession.Printable(session.CollectorVersion) ?? "(version not stated)"}" +
                         $"{(session.AgentVersion is null ? string.Empty : $", agent {CollectorSession.Printable(session.AgentVersion)}")}, from {session.RemoteIP}; {_registry.Count} session(s) open.");
        }

        private void LogClosed(CollectorSession session)
        {
            var caller = session.Caller;
            var end = session.End ?? new SessionEnd("it ended without a recorded cause", Abnormal: true);

            var line = session.IsTable
                ? $"Collector table connection {session.ShortId} of session {session.PairedShortId} closed after {FormatDuration(session.Lifetime)}: {end.Text}. " +
                  $"Product \"{CollectorSession.Printable(caller.Product.DisplayName)}\", client \"{CollectorSession.Printable(session.ClientName)}\", from {session.RemoteIP}; " +
                  $"frames in {session.FramesIn} ({session.BytesIn} bytes), out {session.FramesOut} ({session.BytesOut} bytes); " +
                  $"blocks stored {session.BlocksStored}, duplicate {session.BlocksDuplicate}, all refused {session.BlocksAllRefused}, invalid {session.BlocksInvalid}, " +
                  $"faults {session.BlockFaults}; tables refused {session.TablesRefused}; " +
                  $"last frame from the collector {FormatDuration(session.SinceLastFrame)} before the end; {_registry.TableConnectionCount} table connection(s) open."
                : $"Collector session {session.ShortId} closed after {FormatDuration(session.Lifetime)}: {end.Text}. " +
                  $"Product \"{CollectorSession.Printable(caller.Product.DisplayName)}\", client \"{CollectorSession.Printable(session.ClientName)}\", from {session.RemoteIP}; " +
                  $"frames in {session.FramesIn} ({session.BytesIn} bytes), out {session.FramesOut} ({session.BytesOut} bytes); " +
                  $"values accepted {session.ValuesAccepted}, requests refused {session.RequestsRefused}; " +
                  $"last frame from the collector {FormatDuration(session.SinceLastFrame)} before the end; {_registry.Count} session(s) open.";

            if (end.Abnormal)
                _logger.Warn(line);
            else
                _logger.Info(line);
        }

        // One line per kind of refusal at most once a minute (the same throttle as the limit
        // warnings): a collector that retries every second with a wrong key must not flood
        // the log. The line says how many refusals of that kind went unlogged before it.
        private void LogRefusal(HttpContext context, RefusalThrottle throttle, int status, string reason, LogLevel level)
        {
            if (!IsTimeToWarn(ref throttle.WarnedAtTicks))
            {
                Interlocked.Increment(ref throttle.Unlogged);
                return;
            }

            var unlogged = Interlocked.Exchange(ref throttle.Unlogged, 0);
            var address = context.Connection.RemoteIpAddress;

            if (address is { IsIPv4MappedToIPv6: true })
                address = address.MapToIPv4();

            var client = CollectorSession.Printable(ReadFact(context, SessionProtocol.ClientNameHeader));

            _logger.Log(level, $"A collector session upgrade is refused with {status}: {reason}. Client \"{client ?? "(no ClientName)"}\" from {address?.ToString() ?? "an unknown address"}" +
                               $"{(unlogged > 0 ? $"; {unlogged} more refusal(s) of this kind since the last such line were not logged" : string.Empty)}.");
        }

        // "850 ms", "12.3 s", "5m 03s", "3h 12m 05s": the native collector's format.
        internal static string FormatDuration(TimeSpan duration)
        {
            var ms = Math.Max(0L, (long)duration.TotalMilliseconds);

            if (ms < 1000)
                return $"{ms} ms";

            if (ms < 60_000)
                return $"{ms / 1000}.{ms % 1000 / 100} s";

            var seconds = ms / 1000;

            return seconds < 3600
                ? $"{seconds / 60}m {seconds % 60:00}s"
                : $"{seconds / 3600}h {seconds / 60 % 60:00}m {seconds % 60:00}s";
        }

        // A refused collector cannot deliver until a slot is free: the session is its
        // transport, and nothing falls back to the HTTP routes. So this must reach the
        // operator. At most one line a minute: a fleet over the cap retries.
        private void WarnCapReached(PublicApiRequestInfo caller)
        {
            if (!IsTimeToWarn(ref _capWarnedAtTicks))
                return;

            _logger.Warn($"The collector session limit is reached (CollectorSessions.MaxSessions = {_config.MaxSessions}): the upgrade of {caller.TelemetryPath} from {caller.RemoteIP} " +
                         "is refused with 503 and it cannot send until a session ends. Raise the limit: this server has more collectors than that.");
        }

        // The usual cause is one host whose collector opens sessions without closing them.
        // The other is a reverse proxy the server does not trust: then every collector
        // comes from the proxy's address and they share one limit.
        private void WarnAddressLimitReached(PublicApiRequestInfo caller)
        {
            _logger.Warn($"The collector session limit per address is reached (CollectorSessions.MaxSessionsPerAddress = {_config.MaxSessionsPerAddress}): the upgrade of {caller.TelemetryPath} " +
                         $"from {caller.RemoteIP} is refused with 429. Either that host opens sessions without closing them, or {caller.RemoteIP} is a reverse proxy " +
                         "that is not listed in Kestrel.TrustedProxies, so all collectors share its address.");
        }

        // True at most once a minute for each of the refusals that are worth a log line.
        private bool IsTimeToWarn(ref long warnedAtTicks)
        {
            var now = _time.GetUtcNow().UtcTicks;
            var last = Interlocked.Read(ref warnedAtTicks);

            return now - last >= TimeSpan.TicksPerMinute && Interlocked.CompareExchange(ref warnedAtTicks, now, last) == last;
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


        // `retryAfterSeconds` is for the refusals that pass by themselves: a hint not to come
        // back at once. A collector has its own backoff; this only says the wait is expected.
        private static Task Refuse(HttpContext context, int status, string reason, int? retryAfterSeconds = null)
        {
            context.Response.StatusCode = status;

            if (retryAfterSeconds is { } seconds)
                context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return context.Response.WriteAsync(reason);
        }

        private static string ReadFact(HttpContext context, string header) =>
            context.TryReadInfo(header, out var value) && !string.IsNullOrWhiteSpace(value) ? Truncate(value.Trim()) : null;

        // A close reason is at most 123 bytes on the wire; header facts are kept short too.
        private static string Truncate(string text) => text is null || text.Length <= 100 ? text : text[..100];

        private static bool IsConnectionEnd(Exception ex) =>
            ex is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException;


        private sealed class RefusalThrottle
        {
            public long WarnedAtTicks;

            public long Unlogged;
        }


        // Ends a session that makes no progress for `limit` while the loop waits for the
        // collector: for its next frame, or for it to take what the server sends.
        private sealed class SilenceWatch(TimeProvider time, CollectorSession session, TimeSpan limit, string reason = SilenceReason) : IAsyncDisposable
        {
            private readonly ITimer _timer = time.CreateTimer(_ => session.Abort(reason), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);


            public void Arm() => _timer.Change(limit, Timeout.InfiniteTimeSpan);

            public void Disarm() => _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            public ValueTask DisposeAsync() => _timer.DisposeAsync();
        }
    }
}
