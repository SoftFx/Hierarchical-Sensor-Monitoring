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
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json.Serialization;
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


    // The real Sensor API on a real Kestrel listener (loopback, ephemeral port): the real
    // TelemetryMiddleware, SensorsController, ingestion service and session handler over
    // the cache the test hands in. Only the server's self-monitoring collector and its
    // settings file are replaced. A real listener, not TestServer, because the session is
    // a WebSocket and TestServer substitutes its own in-memory WebSocket for the real one.
    internal sealed class SensorApiTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;


        public RecordingTelemetry Telemetry { get; }

        public CollectorSessionRegistry Registry => _app.Services.GetRequiredService<CollectorSessionRegistry>();

        public Uri HttpBase { get; }

        public Uri SessionUri { get; }


        private SensorApiTestHost(WebApplication app, RecordingTelemetry telemetry)
        {
            _app = app;
            Telemetry = telemetry;

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

            // The part of the server pipeline the Sensor API runs through, in its order.
            app.UseRouting();
            app.UseWebSockets();
            app.UseMiddleware<TelemetryMiddleware>();
            app.MapControllers();

            await app.StartAsync();

            return new SensorApiTestHost(app, telemetry);
        }

        public Task StopAsync() => _app.StopAsync();

        public ValueTask DisposeAsync() => _app.DisposeAsync();


        private sealed class OnlySensorsController : ControllerFeatureProvider
        {
            protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(SensorsController);
        }
    }
}
