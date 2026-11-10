using System;
using System.Text.Json.Serialization;

namespace HSMServer.ServerConfiguration
{
    // Collector sessions (#1664, epic #1660, ADR-0017): one WebSocket per collector at
    // GET /api/sensors/session. Read once at startup, like the Kestrel section: a change
    // takes effect on the next server start.
    public sealed class CollectorSessionsConfig
    {
        // One session is one collector process. An idle session holds about 22 KiB on the
        // server, so the default cap costs about 22 MB when it is reached; an installation
        // with more collectors than this raises it.
        public const int DefaultMaxSessions = 1_000;
        public const int DefaultHeartbeatSeconds = 30;
        public const int DefaultMaxFrameBytes = 4 * 1024 * 1024;

        private const int MinHeartbeatSeconds = 5;
        private const int MaxHeartbeatSeconds = 3600;
        private const int MinFrameBytes = 64 * 1024;
        private const int MaxFrameBytesCeiling = 64 * 1024 * 1024;

        // The server-wide cap on concurrently open sessions. An upgrade over the cap is
        // refused with 503 and the collector stays on HTTP. Zero turns sessions off: the
        // endpoint answers 404 and HTTP responses stop advertising the capability, so no
        // collector ever tries.
        public int MaxSessions { get; set; } = DefaultMaxSessions;

        // Announced to the collector in the hello frame: it sends an application ping after
        // this long without sending anything. The server drops a session after three of
        // these intervals without any inbound frame.
        public int HeartbeatSeconds { get; set; } = DefaultHeartbeatSeconds;

        // Announced in the hello frame as well. A collector routes a larger body to HTTP;
        // a frame over the limit closes the session with 1009.
        public int MaxFrameBytes { get; set; } = DefaultMaxFrameBytes;

        [JsonIgnore]
        public bool Enabled => MaxSessions > 0;

        [JsonIgnore]
        public TimeSpan Heartbeat => TimeSpan.FromSeconds(HeartbeatSeconds);

        // How long a session may stay silent before the server drops it.
        [JsonIgnore]
        public TimeSpan SilenceLimit => TimeSpan.FromSeconds(3L * HeartbeatSeconds);


        public void Validate()
        {
            const string section = nameof(IServerConfig.CollectorSessions);

            if (MaxSessions < 0)
                throw new InvalidOperationException($"{section}.{nameof(MaxSessions)} = {MaxSessions} is invalid: use 0 to turn collector sessions off, or a positive limit.");

            if (HeartbeatSeconds is < MinHeartbeatSeconds or > MaxHeartbeatSeconds)
                throw new InvalidOperationException($"{section}.{nameof(HeartbeatSeconds)} = {HeartbeatSeconds} is invalid: expected {MinHeartbeatSeconds}..{MaxHeartbeatSeconds}.");

            if (MaxFrameBytes is < MinFrameBytes or > MaxFrameBytesCeiling)
                throw new InvalidOperationException($"{section}.{nameof(MaxFrameBytes)} = {MaxFrameBytes} is invalid: expected {MinFrameBytes}..{MaxFrameBytesCeiling}.");
        }
    }
}
