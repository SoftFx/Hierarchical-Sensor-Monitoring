using HSMServer.Core.Cache;
using HSMServer.Middleware.Telemetry;
using NLog;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions
{
    // Directive push over a collector session (#1660 step 5, ADR-0017). Directives are state,
    // not events, and stay level-triggered as on the HTTP header path:
    //   - right after the hello, the whole set, in the order of the headers;
    //   - every CheckPeriod, the set is computed again and every directive that was not in the
    //     set computed last is pushed, in order: a sensor group switched, a newer agent staged;
    //   - every ReannouncePeriod, the whole set again. That is what retries a failed agent
    //     update, which the repetition on every HTTP answer does over HTTP.
    // One frame per directive: {"op":"directive","body":"<the text of one header line>"}.
    //
    // Only for a session whose upgrade stated X-Agent-Version (the handler decides). Both
    // periods run on the server's TimeProvider. A push goes through the session's serialized
    // send like an ack; once the server is closing the session, or the connection is gone,
    // nothing more is pushed and the loop ends quietly: the handler loop reports the session. A
    // send that fails while the session is up is logged (one line a minute at most) and the loop
    // goes on: what was not sent is pushed again at the next check, in order.
    internal sealed class DirectivePush : IAsyncDisposable
    {
        public static readonly TimeSpan CheckPeriod = AgentDirectives.StagedVersionCacheAge;
        public static readonly TimeSpan ReannouncePeriod = TimeSpan.FromMinutes(5);

        // How often at most a failing push is logged for one session.
        private static readonly TimeSpan FailureLogPeriod = TimeSpan.FromMinutes(1);

        private readonly CollectorSession _session;
        private readonly AgentDirectives _directives;
        private readonly ITreeValuesCache _cache;
        private readonly ISensorApiTelemetry _telemetry;
        private readonly Logger _logger;
        private readonly TimeProvider _time;
        private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _send;

        // Touched by the push loop only.
        private long? _failureLoggedAt;
        private long _unloggedFailures;

        private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        private readonly CancellationTokenSource _stop = new();
        private readonly ITimer _check;
        private readonly ITimer _reannounce;
        private readonly Task _loop;

        // 1 when the next round pushes the whole set: the first round, and every re-announcement.
        private int _wholeSetDue = 1;


        // `send` writes one frame to the session: the session's own serialized send, which tests
        // replace to make a send fail.
        public DirectivePush(CollectorSession session, AgentDirectives directives, ITreeValuesCache cache, ISensorApiTelemetry telemetry, Logger logger, TimeProvider time,
            Func<ReadOnlyMemory<byte>, CancellationToken, Task> send)
        {
            _session = session;
            _directives = directives;
            _cache = cache;
            _telemetry = telemetry;
            _logger = logger;
            _time = time;
            _send = send;

            _check = time.CreateTimer(_ => Wake(), null, CheckPeriod, CheckPeriod);
            _reannounce = time.CreateTimer(_ =>
            {
                Volatile.Write(ref _wholeSetDue, 1);
                Wake();
            }, null, ReannouncePeriod, ReannouncePeriod);

            Wake();

            _loop = Task.Run(() => RunAsync(_stop.Token));
        }


        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();

            await _check.DisposeAsync();
            await _reannounce.DisposeAsync();

            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }

            _stop.Dispose();
        }


        private void Wake() => _wake.Writer.TryWrite(true);

        private async Task RunAsync(CancellationToken token)
        {
            // What the collector has been told and still holds, by text: only what was actually
            // sent. A directive whose send failed stays out of it, so the next check pushes it again.
            var told = new HashSet<string>(StringComparer.Ordinal);
            var output = new ArrayBufferWriter<byte>(128);

            try
            {
                while (await _wake.Reader.WaitToReadAsync(token))
                {
                    _wake.Reader.TryRead(out _);

                    if (IsEnding(token))
                        return;

                    var whole = Interlocked.Exchange(ref _wholeSetDue, 0) == 1;

                    if (!TryCompute(out var set))
                    {
                        // Who the session acts for cannot be read now. The handler loop closes the
                        // session when its access is gone; until then the next round tries again,
                        // and a re-announcement that was due stays due.
                        if (whole)
                            Volatile.Write(ref _wholeSetDue, 1);

                        continue;
                    }

                    var push = whole ? set : set.Where(directive => !told.Contains(directive)).ToList();

                    // A re-announcement tells the collector the whole set again: what of it fails
                    // to go out is pushed at the next check. Otherwise the collector still holds
                    // what it was told and what is still in the set.
                    if (whole)
                        told.Clear();
                    else
                        told.IntersectWith(set);

                    var sent = 0;

                    foreach (var directive in push)
                    {
                        if (IsEnding(token))
                            return;

                        output.Clear();
                        SessionProtocol.WriteDirective(output, directive);

                        try
                        {
                            await _send(output.WrittenMemory, token);
                        }
                        catch (Exception) when (IsEnding(token))
                        {
                            // The session ended, or is ending: nothing to push to. Not an error of
                            // the server; the handler loop reports the session.
                            return;
                        }
                        catch (Exception ex)
                        {
                            // The session is up and this send failed. The rest of the round waits,
                            // so the order of the set holds; the next check pushes what is not in
                            // `told`, this directive first.
                            ReportFailedSend(ex);
                            break;
                        }

                        _telemetry.AddResponse(_session.Caller.TelemetryPath, output.WrittenCount);
                        told.Add(directive);
                        sent++;
                    }

                    if (sent > 0)
                        _logger.Debug($"Collector session {_session.ShortId}: pushed {sent} directive(s){(whole ? " of the whole set" : string.Empty)}.");
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException)
            {
                // The session ended, or is ending: nothing to push to. Not an error of the server.
            }
            catch (Exception ex)
            {
                _logger.Error($"Directive push of collector session {_session.ShortId} failed: {ex}");
            }
        }

        private bool IsEnding(CancellationToken token) => token.IsCancellationRequested || _session.IsClosing || !_session.IsOpen;

        // One line a minute at most for a session whose pushes keep failing (they are tried again
        // every check), with the number of failures that went unlogged since the previous line.
        private void ReportFailedSend(Exception ex)
        {
            var now = _time.GetTimestamp();

            if (_failureLoggedAt is { } last && _time.GetElapsedTime(last, now) < FailureLogPeriod)
            {
                _unloggedFailures++;
                return;
            }

            var unlogged = _unloggedFailures;

            _failureLoggedAt = now;
            _unloggedFailures = 0;

            _logger.Error($"A directive push of collector session {_session.ShortId} failed; it is pushed again at the next check" +
                          $"{(unlogged > 0 ? $" ({unlogged} more failure(s) since the previous line)" : string.Empty)}: {ex}");
        }

        // The directives of this session now: its key and product are looked up again, as every
        // HTTP request looks them up, so a group switched in the UI is seen at the next round.
        private bool TryCompute(out List<string> set)
        {
            set = null;

            try
            {
                if (!_cache.TryGetKey(_session.KeyId, out var key, out _) || !_cache.TryGetRootProduct(key.ProductId, out var product, out _))
                    return false;

                set = _directives.For(_session.AgentVersion, product);

                return true;
            }
            catch (Exception ex)
            {
                // A failure of the server's state (the cache) is not a reason to end the session.
                _logger.Debug($"Collector session {_session.ShortId}: the directives cannot be computed now: {ex.Message}");

                return false;
            }
        }
    }
}
