using System;
using System.Text;
using System.Text.Json;

namespace HSMDataCollector.Client.Session
{
    /// <summary>
    /// The wire of a collector session, version 1 (epic #1660, ADR-0017), as the collector
    /// speaks it. The contract is aicontext/features/api/wire-contract/feature.md.
    ///
    /// Text frames, one JSON object each:
    ///   collector to server  {"id":17,"op":"list","body":[...]}            a request
    ///   server to collector  {"ack":17,"status":200,"body":{...}}          its answer
    ///                        {"ack":17,"status":400,"error":"..."}         its answer, failed
    ///   server to collector  {"op":"hello","body":{...}}                   the first frame
    ///   either               {"op":"ping"} / {"op":"pong"}                 heartbeat
    ///
    /// The WebSocket protocol itself is <c>System.Net.WebSockets.ClientWebSocket</c>'s; nothing
    /// here frames, masks or parses a WebSocket frame. The JSON envelope is read with
    /// System.Text.Json.
    /// </summary>
    internal static class SessionProtocol
    {
        internal const string Subprotocol = "hsm.sensors.v1";
        internal const string EndpointSuffix = "/session";

        internal const string CollectorKindHeader = "X-Collector-Kind";
        internal const string CollectorVersionHeader = "X-Collector-Version";
        internal const string CollectorKind = "managed";

        internal const string OpList = "list";
        internal const string OpCommands = "commands";
        internal const string OpHello = "hello";
        internal const string OpPing = "ping";
        internal const string OpPong = "pong";
        internal const string OpDirective = "directive";

        /// <summary>Bytes kept free in a frame for the envelope around a body.</summary>
        internal const int EnvelopeRoom = 64;

        // What a hello may state. Anything else is a server this collector cannot talk to: a
        // frame limit no value fits, a heartbeat that would spin or never fire.
        internal const int MinFrameBytes = 1024;
        internal const int MinHeartbeatSeconds = 1;
        internal const int MaxHeartbeatSeconds = 3600;

        /// <summary>
        /// The largest frame the collector takes from the server (8 MiB, the native collector's
        /// bound). The server states no limit for its own frames; a success ack is a dictionary
        /// of per-path errors and stays far below this.
        /// </summary>
        internal const int MaxInboundFrameBytes = 8 * 1024 * 1024;

        internal static readonly byte[] Ping = Encoding.UTF8.GetBytes("{\"op\":\"ping\"}");
        internal static readonly byte[] Pong = Encoding.UTF8.GetBytes("{\"op\":\"pong\"}");

        /// <summary>
        /// {"id":N,"op":"...","body":...} with <paramref name="body"/> embedded as it is: the
        /// body is the JSON the HTTP route of the same name takes, byte for byte.
        /// </summary>
        internal static byte[] BuildRequest(long id, string op, string body)
        {
            var text = new StringBuilder(body.Length + EnvelopeRoom)
                .Append("{\"id\":").Append(id)
                .Append(",\"op\":\"").Append(op)
                .Append("\",\"body\":").Append(body)
                .Append('}');

            return Encoding.UTF8.GetBytes(text.ToString());
        }
    }


    /// <summary>What became of one request on the session.</summary>
    internal readonly struct SessionAck
    {
        private SessionAck(bool acked, int status, string error, string body)
        {
            Acked = acked;
            Status = status;
            Error = error;
            Body = body;
        }

        /// <summary>
        /// An ack arrived. False when the request never got one: no session, the session
        /// ended, or the wait timed out.
        /// </summary>
        internal bool Acked { get; }

        /// <summary>The ack's status: the HTTP status the same request would have got.</summary>
        internal int Status { get; }

        /// <summary>Why there is no ack, or the server's <c>error</c> text of a failure ack.</summary>
        internal string Error { get; }

        /// <summary>The JSON of a success ack's <c>body</c> (the per-path error dictionary).</summary>
        internal string Body { get; }

        internal bool IsSuccess => Acked && Status >= 200 && Status < 300;

        internal static SessionAck Answered(int status, string error, string body) => new SessionAck(true, status, error, body);

        internal static SessionAck NoAck(string why) => new SessionAck(false, 0, why, null);
    }


    /// <summary>One frame from the server, as far as the collector reads it.</summary>
    internal readonly struct SessionFrame
    {
        internal long? Ack { get; }

        internal int Status { get; }

        internal string Op { get; }

        internal string Error { get; }

        /// <summary>The raw JSON of <c>body</c>, or null when the frame has none.</summary>
        internal string Body { get; }

        internal int MaxFrameBytes { get; }

        internal int HeartbeatSeconds { get; }

        private SessionFrame(long? ack, int status, string op, string error, string body, int maxFrameBytes, int heartbeatSeconds)
        {
            Ack = ack;
            Status = status;
            Op = op;
            Error = error;
            Body = body;
            MaxFrameBytes = maxFrameBytes;
            HeartbeatSeconds = heartbeatSeconds;
        }

        /// <summary>
        /// Reads the envelope fields this version knows and ignores every other, which is what
        /// lets version 1 grow. False when the text is not a JSON object.
        /// </summary>
        internal static bool TryParse(string text, out SessionFrame frame)
        {
            frame = default;

            try
            {
                using (var document = JsonDocument.Parse(text))
                {
                    var root = document.RootElement;

                    if (root.ValueKind != JsonValueKind.Object)
                        return false;

                    long? ack = null;
                    if (root.TryGetProperty("ack", out var ackElement) && ackElement.ValueKind == JsonValueKind.Number && ackElement.TryGetInt64(out var ackId))
                        ack = ackId;

                    var status = root.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.Number && statusElement.TryGetInt32(out var parsedStatus)
                        ? parsedStatus
                        : 0;

                    var op = ReadString(root, "op");
                    var error = ReadString(root, "error");

                    string body = null;
                    var maxFrameBytes = 0;
                    var heartbeatSeconds = 0;

                    if (root.TryGetProperty("body", out var bodyElement))
                    {
                        // A directive's body is a string and is taken as its value; everything
                        // else is kept as the JSON it is.
                        body = bodyElement.ValueKind == JsonValueKind.String ? bodyElement.GetString() : bodyElement.GetRawText();

                        if (bodyElement.ValueKind == JsonValueKind.Object)
                        {
                            maxFrameBytes = ReadInt(bodyElement, "maxFrameBytes");
                            heartbeatSeconds = ReadInt(bodyElement, "heartbeatSeconds");
                        }
                    }

                    frame = new SessionFrame(ack, status, op, error, body, maxFrameBytes, heartbeatSeconds);

                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string ReadString(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

        private static int ReadInt(JsonElement parent, string name) =>
            parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) ? value : 0;
    }
}
