using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Middleware.Telemetry;
using HSMServer.SensorApi;
using HSMServer.SensorApi.Sessions;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.Tests.SensorApi
{
    public sealed class CollectorSessionFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(CollectorSessionTests);
    }


    // What the Sensor API counted, in the order it counted it.
    internal sealed class RecordingTelemetry : ISensorApiTelemetry
    {
        public ConcurrentQueue<(string Kind, string Path, long Amount)> Records { get; } = new();


        public void AddRequest(string telemetryPath, long? bodyBytes) => Records.Enqueue(("request", telemetryPath, bodyBytes ?? 0));

        public void AddResponse(string telemetryPath, long? bodyBytes) => Records.Enqueue(("response", telemetryPath, bodyBytes ?? 0));

        public void AddReceivedValues(string telemetryPath, int count) => Records.Enqueue(("values", telemetryPath, count));

        public long Sum(string kind, string path) => Records.Where(record => record.Kind == kind && record.Path == path).Sum(record => record.Amount);
    }


    // What the session handler logged, in order. Its own LogFactory, so the process-wide NLog
    // configuration is neither read nor changed and the tests can run in parallel.
    internal sealed class RecordingLog : Target
    {
        public ConcurrentQueue<(NLog.LogLevel Level, string Message)> Lines { get; } = new();


        public static Logger CreateLogger(RecordingLog target)
        {
            var factory = new LogFactory();
            var config = new LoggingConfiguration(factory);

            config.AddRuleForAllLevels(target);
            factory.Configuration = config;

            return factory.GetLogger(nameof(CollectorSessionHandler));
        }

        public (NLog.LogLevel Level, string Message)[] Matching(params string[] parts) =>
            Lines.Where(line => parts.All(part => line.Message.Contains(part, StringComparison.Ordinal))).ToArray();

        public string Dump() => string.Join(Environment.NewLine, Lines.Select(line => $"{line.Level}|{line.Message}"));


        protected override void Write(LogEventInfo logEvent) => Lines.Enqueue((logEvent.Level, logEvent.FormattedMessage));
    }


    // A clock whose timers fire only when a test says so. The session's silence limit runs
    // on it, so no test waits for real seconds and none can be cut off by a slow machine.
    internal sealed class ManualTimeProvider : TimeProvider
    {
        public ConcurrentQueue<ManualTimer> Timers { get; } = new();


        public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);

            Timers.Enqueue(timer);

            return timer;
        }
    }


    internal sealed class ManualTimer(TimerCallback callback, object state, TimeSpan dueTime) : ITimer
    {
        private long _dueTicks = dueTime.Ticks;
        private int _arms;


        // What the timer was last set to; Timeout.InfiniteTimeSpan when it is not running.
        public TimeSpan DueTime => TimeSpan.FromTicks(Volatile.Read(ref _dueTicks));

        public bool IsArmed => DueTime != Timeout.InfiniteTimeSpan;

        // How many times it was started or restarted.
        public int Arms => Volatile.Read(ref _arms);


        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Volatile.Write(ref _dueTicks, dueTime.Ticks);

            if (dueTime != Timeout.InfiniteTimeSpan)
                Interlocked.Increment(ref _arms);

            return true;
        }

        // The due time has passed.
        public void Fire() => callback(state);

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }


    // The real cache, except that a test can hold one batch inside the store: everything is
    // forwarded, and AddSensorValuesAsync waits at the gate when one is set.
    public class GatedCache : DispatchProxy
    {
        private ITreeValuesCache _inner;
        private volatile TaskCompletionSource _gate;
        private volatile bool _hold;


        public ITreeValuesCache Cache { get; private set; }

        // Completes when a batch has reached the store and is waiting at the gate.
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);


        public static GatedCache Wrap(ITreeValuesCache inner)
        {
            var cache = Create<ITreeValuesCache, GatedCache>();
            var proxy = (GatedCache)(object)cache;

            proxy._inner = inner;
            proxy.Cache = cache;

            return proxy;
        }

        public void HoldTheNextStore()
        {
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _hold = true;
        }

        public void Release() => _gate?.TrySetResult();


        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (_hold && targetMethod.Name == nameof(ITreeValuesCache.AddSensorValuesAsync))
            {
                _hold = false;

                return StoreAfter(_gate, targetMethod, args);
            }

            return targetMethod.Invoke(_inner, args);
        }

        private async Task<Dictionary<string, string>> StoreAfter(TaskCompletionSource gate, MethodInfo store, object[] args)
        {
            Entered.TrySetResult();

            await gate.Task;

            return await (Task<Dictionary<string, string>>)store.Invoke(_inner, args);
        }
    }


    // The real Sensor API on a real Kestrel listener (loopback, ephemeral port): the real
    // TelemetryMiddleware, SensorsController, ingestion service and session handler over
    // the cache the test hands in. Only the server's self-monitoring collector and its
    // settings file are replaced. A real listener, not TestServer, because the session is
    // a WebSocket and TestServer substitutes its own in-memory WebSocket for the real one.
    internal sealed class SensorApiTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;


        public RecordingTelemetry Telemetry { get; }

        public RecordingLog Log { get; }

        public ManualTimeProvider Time => (ManualTimeProvider)_app.Services.GetRequiredService<TimeProvider>();

        public CollectorSessionRegistry Registry => _app.Services.GetRequiredService<CollectorSessionRegistry>();

        public Uri HttpBase { get; }

        public Uri SessionUri { get; }


        private SensorApiTestHost(WebApplication app, RecordingTelemetry telemetry, RecordingLog log)
        {
            _app = app;
            Telemetry = telemetry;
            Log = log;

            var address = new Uri(app.Urls.First());

            HttpBase = new Uri($"http://127.0.0.1:{address.Port}/api/sensors/");
            SessionUri = new Uri($"ws://127.0.0.1:{address.Port}/api/sensors/session");
        }


        public static async Task<SensorApiTestHost> StartAsync(ITreeValuesCache cache, CollectorSessionsConfig sessions)
        {
            var telemetry = new RecordingTelemetry();

            var config = new Mock<IServerConfig>();
            config.SetupGet(c => c.CollectorSessions).Returns(sessions);
            config.SetupGet(c => c.Agent).Returns(new AgentConfig());

            var builder = WebApplication.CreateBuilder();

            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));

            builder.Services.AddSingleton(cache);
            builder.Services.AddSingleton<ISensorApiTelemetry>(telemetry);
            builder.Services.AddSingleton(config.Object);
            builder.Services.AddSingleton<TelemetryCollector>();
            builder.Services.AddSingleton<SensorIngestionService>();
            builder.Services.AddSingleton<CollectorSessionRegistry>();
            builder.Services.AddSingleton<CollectorSessionHandler>();
            builder.Services.AddSingleton<TimeProvider>(new ManualTimeProvider());

            builder.Services.AddControllers()
                .ConfigureApplicationPartManager(manager =>
                {
                    manager.ApplicationParts.Clear();
                    manager.ApplicationParts.Add(new AssemblyPart(typeof(SensorsController).Assembly));

                    foreach (var provider in manager.FeatureProviders.OfType<ControllerFeatureProvider>().ToList())
                        manager.FeatureProviders.Remove(provider);

                    manager.FeatureProviders.Add(new OnlySensorsController());
                })
                // As in Program.cs.
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals);

            var app = builder.Build();
            var log = new RecordingLog();

            app.Services.GetRequiredService<CollectorSessionHandler>().UseLogger(RecordingLog.CreateLogger(log));

            // The part of the server pipeline the Sensor API runs through, in its order.
            app.UseRouting();
            app.UseWebSockets();
            app.UseMiddleware<TelemetryMiddleware>();
            app.MapControllers();

            await app.StartAsync();

            return new SensorApiTestHost(app, telemetry, log);
        }

        public Task StopAsync() => _app.StopAsync();

        public ValueTask DisposeAsync() => _app.DisposeAsync();


        private sealed class OnlySensorsController : ControllerFeatureProvider
        {
            protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(SensorsController);
        }
    }
}
