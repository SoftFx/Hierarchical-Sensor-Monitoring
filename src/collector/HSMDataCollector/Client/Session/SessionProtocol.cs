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

        /// <summary>The subprotocol of the table connection (#1845, ADR-0021 point 7): a second WebSocket of the session.</summary>
        internal const string TablesSubprotocol = "hsm.tables.v1";

        /// <summary>The request header a table connection names its session in: the <c>sessionId</c> of the session's hello.</summary>
        internal const string SessionHeader = "X-Hsm-Session";

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

        /// <summary>
        /// The largest text frame the collector takes on a table connection: an ack lists at most 100 refused tables,
        /// each name cut to 256 characters, so it stays within a few hundred kilobytes; a larger frame is a broken or
        /// hostile server and closes the table connection.
        /// </summary>
        internal const int MaxTableInboundFrameBytes = 1024 * 1024;

        internal static readonly byte[] Ping = Encoding.UTF8.GetBytes("{\"op\":\"ping\"}");
        internal static readonly byte[] Pong = Encoding.UTF8.GetBytes("{\"op\":\"pong\"}");

        /// <summary>
        /// {"id":N,"op":"...","body":...} with <paramref name="body"/> embedded as it is: the
        /// body is the JSON the HTTP route of the same name takes, byte for byte.
        /// </summary>
        internal static byte[] BuildRequest(long id, string op, string body)
        {
            var head = $"{{\"id\":{id},\"op\":\"{op}\",\"body\":";
            var utf8 = Encoding.UTF8;

            // Head, body and the closing brace written into one array: the body is the large
            // part and is not copied into an intermediate string first.
            var frame = new byte[utf8.GetByteCount(head) + utf8.GetByteCount(body) + 1];
            var written = utf8.GetBytes(head, 0, head.Length, frame, 0);

            written += utf8.GetBytes(body, 0, body.Length, frame, written);
            frame[written] = (byte)'}';

            return frame;
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

        /// <summary>
        /// The <c>body</c>: the value of a string, the raw JSON of anything else, or null when the
        /// frame has none.
        /// </summary>
        internal string Body { get; }

        /// <summary>True when <c>body</c> is a JSON string (a directive's body is one).</summary>
        internal bool BodyIsString { get; }

        internal int MaxFrameBytes { get; }

        internal int HeartbeatSeconds { get; }

        /// <summary>The <c>sessionId</c> of a session's hello (#1845): what its table connection names; null when absent.</summary>
        internal string SessionId { get; }

        /// <summary>The <c>retryAfter</c> of a failure ack, in seconds; null when absent.</summary>
        internal int? RetryAfter { get; }

        /// <summary>
        /// The <c>blockLimits</c> of a table connection's hello as they were sent (null when absent): the limits a block
        /// is checked against. Values the format cannot take are kept as they are, for the caller to refuse.
        /// </summary>
        internal TableHelloLimits BlockLimits { get; }

        private SessionFrame(long? ack, int status, string op, string error, string body, bool bodyIsString, int maxFrameBytes, int heartbeatSeconds,
                             string sessionId, int? retryAfter, TableHelloLimits blockLimits)
        {
            Ack = ack;
            Status = status;
            Op = op;
            Error = error;
            Body = body;
            BodyIsString = bodyIsString;
            MaxFrameBytes = maxFrameBytes;
            HeartbeatSeconds = heartbeatSeconds;
            SessionId = sessionId;
            RetryAfter = retryAfter;
            BlockLimits = blockLimits;
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

                    int? retryAfter = null;
                    if (root.TryGetProperty("retryAfter", out var retryElement) && retryElement.ValueKind == JsonValueKind.Number && retryElement.TryGetInt32(out var seconds))
                        retryAfter = seconds;

                    string sessionId = null;
                    TableHelloLimits blockLimits = null;

                    string body = null;
                    var bodyIsString = false;
                    var maxFrameBytes = 0;
                    var heartbeatSeconds = 0;

                    if (root.TryGetProperty("body", out var bodyElement))
                    {
                        // A directive's body is a string and is taken as its value; everything
                        // else is kept as the JSON it is.
                        bodyIsString = bodyElement.ValueKind == JsonValueKind.String;
                        body = bodyIsString ? bodyElement.GetString() : bodyElement.GetRawText();

                        if (bodyElement.ValueKind == JsonValueKind.Object)
                        {
                            maxFrameBytes = ReadInt(bodyElement, "maxFrameBytes");
                            heartbeatSeconds = ReadInt(bodyElement, "heartbeatSeconds");
                            sessionId = ReadString(bodyElement, "sessionId");

                            if (bodyElement.TryGetProperty("blockLimits", out var limits) && limits.ValueKind == JsonValueKind.Object)
                                blockLimits = new TableHelloLimits(
                                    ReadInt(limits, "maxDecompressedBytes"),
                                    ReadInt(limits, "maxTables"),
                                    ReadInt(limits, "maxRows"),
                                    ReadInt(limits, "maxColumnsPerTable"),
                                    ReadInt(limits, "maxTableIdLength"));
                        }
                    }

                    frame = new SessionFrame(ack, status, op, error, body, bodyIsString, maxFrameBytes, heartbeatSeconds, sessionId, retryAfter, blockLimits);

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


    /// <summary>The <c>blockLimits</c> of a table connection's hello, as sent (0 for a field that is missing or not a number).</summary>
    internal sealed class TableHelloLimits
    {
        internal TableHelloLimits(int maxDecompressedBytes, int maxTables, int maxRows, int maxColumnsPerTable, int maxTableIdLength)
        {
            MaxDecompressedBytes = maxDecompressedBytes;
            MaxTables = maxTables;
            MaxRows = maxRows;
            MaxColumnsPerTable = maxColumnsPerTable;
            MaxTableIdLength = maxTableIdLength;
        }

        internal int MaxDecompressedBytes { get; }

        internal int MaxTables { get; }

        internal int MaxRows { get; }

        internal int MaxColumnsPerTable { get; }

        internal int MaxTableIdLength { get; }
    }
}
