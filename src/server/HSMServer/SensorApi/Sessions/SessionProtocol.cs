using Microsoft.AspNetCore.Http;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text.Json;
using HSMServer.Core.TableSensors;
using HSMTableBlocks;

namespace HSMServer.SensorApi.Sessions
{
    // The wire of a collector session, version 1 (#1664, epic #1660, ADR-0017). Documented
    // in aicontext/features/api/wire-contract/feature.md, which is the contract; this file
    // is its only implementation on the server.
    //
    // Text frames, one JSON object each:
    //   collector to server  {"id":17,"op":"list","body":[...]}            a request
    //   server to collector  {"ack":17,"status":200,"body":{...}}          its answer
    //                        {"ack":17,"status":400,"error":"..."}         its answer, failed
    //   server to collector  {"op":"hello","body":{...}}                   a push
    //                        {"op":"directive","body":"name:argument"}  a push (#1660 step 5)
    //   either               {"op":"ping"} / {"op":"pong"}                 heartbeat
    //
    // The table connection (#1845, ADR-0021 point 7): a second WebSocket of the same collector,
    // subprotocol hsm.tables.v1, tied to its ordinary session by X-Hsm-Session (the sessionId of
    // the ordinary hello). Its requests are binary frames (TableFrame: a block or a probe); its
    // acks, hello and heartbeat are the text frames above.
    internal static class SessionProtocol
    {
        public const string Subprotocol = "hsm.sensors.v1";
        public const string TablesSubprotocol = "hsm.tables.v1";

        // The ordinary session a table connection belongs to: its id from the hello, 32 hex digits.
        public const string SessionHeader = "X-Hsm-Session";

        // Facts a collector states about itself on the upgrade request, for the registry.
        public const string AgentVersionHeader = "X-Agent-Version";
        public const string CollectorVersionHeader = "X-Collector-Version";
        public const string CollectorKindHeader = "X-Collector-Kind";
        // The collector's name, as on every Sensor API request.
        public const string ClientNameHeader = "ClientName";

        public const string OpList = "list";
        public const string OpCommands = "commands";
        public const string OpPing = "ping";
        public const string OpPong = "pong";
        public const string OpHello = "hello";
        public const string OpDirective = "directive";

        // The status of an ack is the HTTP status the same request would have got.
        public const int StatusOk = 200;
        public const int StatusBadRequest = 400;
        public const int StatusInternalError = 500;
        public const int StatusUnavailable = 503;

        // What a collector waits before it sends a block again after a store fault: the block
        // store's own backoff after a failed write.
        public const int StoreRetryAfterSeconds = 60;

        public const WebSocketCloseStatus ClosePolicyViolation = WebSocketCloseStatus.PolicyViolation;      // 1008
        public const WebSocketCloseStatus CloseMessageTooBig = WebSocketCloseStatus.MessageTooBig;          // 1009
        public const WebSocketCloseStatus CloseProtocolError = WebSocketCloseStatus.ProtocolError;          // 1002
        public const WebSocketCloseStatus CloseUnsupportedData = WebSocketCloseStatus.InvalidMessageType;   // 1003
        public const WebSocketCloseStatus CloseInvalidPayload = WebSocketCloseStatus.InvalidPayloadData;    // 1007
        public const WebSocketCloseStatus CloseGoingAway = WebSocketCloseStatus.EndpointUnavailable;        // 1001

        // An ack body is a path-to-error dictionary. No naming policy: the keys are sensor
        // paths and must arrive exactly as the collector sent them.
        private static readonly JsonSerializerOptions _ackBodyOptions = new();

        private static readonly JsonEncodedText _ack = JsonEncodedText.Encode("ack");
        private static readonly JsonEncodedText _status = JsonEncodedText.Encode("status");
        private static readonly JsonEncodedText _body = JsonEncodedText.Encode("body");
        private static readonly JsonEncodedText _error = JsonEncodedText.Encode("error");
        private static readonly JsonEncodedText _op = JsonEncodedText.Encode("op");
        private static readonly JsonEncodedText _maxFrameBytes = JsonEncodedText.Encode("maxFrameBytes");
        private static readonly JsonEncodedText _heartbeatSeconds = JsonEncodedText.Encode("heartbeatSeconds");
        private static readonly JsonEncodedText _sessionId = JsonEncodedText.Encode("sessionId");
        private static readonly JsonEncodedText _blockLimits = JsonEncodedText.Encode("blockLimits");
        private static readonly JsonEncodedText _retryAfter = JsonEncodedText.Encode("retryAfter");
        private static readonly JsonEncodedText _outcome = JsonEncodedText.Encode("outcome");
        private static readonly JsonEncodedText _tables = JsonEncodedText.Encode("tables");
        private static readonly JsonEncodedText _refused = JsonEncodedText.Encode("refused");
        private static readonly JsonEncodedText _refusedKnown = JsonEncodedText.Encode("refusedKnown");
        private static readonly JsonEncodedText _index = JsonEncodedText.Encode("index");
        private static readonly JsonEncodedText _table = JsonEncodedText.Encode("table");
        private static readonly JsonEncodedText _reason = JsonEncodedText.Encode("reason");

        private static readonly byte[] _pong = "{\"op\":\"pong\"}"u8.ToArray();

        private static readonly object _upgradedKey = new();

        public static ReadOnlyMemory<byte> Pong => _pong;


        // Set by the handler once a request became a session. The status code cannot tell:
        // an upgrade answers 101 over HTTP/1.1 and 200 over HTTP/2.
        public static void MarkUpgraded(HttpContext context) => context.Items[_upgradedKey] = true;

        public static bool WasUpgraded(HttpContext context) => context.Items.ContainsKey(_upgradedKey);


        // The hello of an ordinary session. `sessionId` (#1845) is what a table connection of the
        // same collector names in X-Hsm-Session.
        public static void WriteHello(IBufferWriter<byte> output, int maxFrameBytes, int heartbeatSeconds, Guid sessionId)
        {
            using var writer = new Utf8JsonWriter(output);

            writer.WriteStartObject();
            writer.WriteString(_op, OpHello);
            writer.WriteStartObject(_body);
            writer.WriteNumber(_maxFrameBytes, maxFrameBytes);
            writer.WriteNumber(_heartbeatSeconds, heartbeatSeconds);
            writer.WriteString(_sessionId, FormatSessionId(sessionId));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        // The hello of a table connection (#1845): the largest frame, the heartbeat, and the limits
        // a block is checked against, so that a collector splits a tick's block to fit them.
        public static void WriteTableHello(IBufferWriter<byte> output, int maxFrameBytes, int heartbeatSeconds, TableBlockLimits limits)
        {
            using var writer = new Utf8JsonWriter(output);

            writer.WriteStartObject();
            writer.WriteString(_op, OpHello);
            writer.WriteStartObject(_body);
            writer.WriteNumber(_maxFrameBytes, maxFrameBytes);
            writer.WriteNumber(_heartbeatSeconds, heartbeatSeconds);
            writer.WriteStartObject(_blockLimits);
            writer.WriteNumber("maxDecompressedBytes", limits.MaxDecompressedBytes);
            writer.WriteNumber("maxTables", limits.MaxTables);
            writer.WriteNumber("maxRows", limits.MaxRows);
            writer.WriteNumber("maxColumnsPerTable", limits.MaxColumnsPerTable);
            writer.WriteNumber("maxTableIdLength", limits.MaxTableIdLength);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        public static string FormatSessionId(Guid id) => id.ToString("N");

        public static bool TryParseSessionId(string text, out Guid id) => Guid.TryParseExact(text, "N", out id);

        // The answer to a block the store took, found stored before, or refused as a whole by its
        // tables (#1845): 200, the outcome and the refused tables by name and reason. `refused` is
        // null when they are not known (a duplicate whose first put cannot be read back, or, after
        // the merge of #1850, was merged and deleted): the list is then left out and
        // "refusedKnown":false says so.
        public static void WriteBlockAck(IBufferWriter<byte> output, long id, string outcome, int tables, IReadOnlyList<TableRefusalInfo> refused)
        {
            using var writer = new Utf8JsonWriter(output);

            writer.WriteStartObject();
            writer.WriteNumber(_ack, id);
            writer.WriteNumber(_status, StatusOk);
            writer.WriteStartObject(_body);
            writer.WriteString(_outcome, outcome);
            writer.WriteNumber(_tables, tables);

            if (refused is null)
            {
                writer.WriteBoolean(_refusedKnown, false);
                writer.WriteEndObject();
                writer.WriteEndObject();
                return;
            }

            writer.WriteStartArray(_refused);

            foreach (var refusal in refused)
            {
                writer.WriteStartObject();
                writer.WriteNumber(_index, refusal.Index);
                writer.WriteString(_table, refusal.TableId);
                writer.WriteString(_reason, refusal.Reason.ToString());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        // One server directive (#1660 step 5): the text of one X-Hsm-Directive header line.
        public static void WriteDirective(IBufferWriter<byte> output, string directive)
        {
            using var writer = new Utf8JsonWriter(output);

            writer.WriteStartObject();
            writer.WriteString(_op, OpDirective);
            writer.WriteString(_body, directive);
            writer.WriteEndObject();
        }

        // A success ack carries the success body of the matching route.
        public static void WriteAck(IBufferWriter<byte> output, long id, int status, Dictionary<string, string> body)
        {
            using var writer = new Utf8JsonWriter(output);

            writer.WriteStartObject();
            writer.WriteNumber(_ack, id);
            writer.WriteNumber(_status, status);
            writer.WritePropertyName(_body);
            JsonSerializer.Serialize(writer, body, _ackBodyOptions);
            writer.WriteEndObject();
        }

        // A failure ack carries a short reason and never echoes the request, unlike the
        // HTTP 400 of /list.
        public static void WriteAck(IBufferWriter<byte> output, long id, int status, string error)
        {
            using var writer = new Utf8JsonWriter(output);

            writer.WriteStartObject();
            writer.WriteNumber(_ack, id);
            writer.WriteNumber(_status, status);
            writer.WriteString(_error, error);
            writer.WriteEndObject();
        }

        // A failure ack that says when to try again (#1845: a store fault on a table connection).
        public static void WriteAck(IBufferWriter<byte> output, long id, int status, string error, int retryAfterSeconds)
        {
            using var writer = new Utf8JsonWriter(output);

            writer.WriteStartObject();
            writer.WriteNumber(_ack, id);
            writer.WriteNumber(_status, status);
            writer.WriteString(_error, error);
            writer.WriteNumber(_retryAfter, retryAfterSeconds);
            writer.WriteEndObject();
        }
    }


    // The envelope of one inbound frame. `Body` is only valid until the frame's document
    // is disposed.
    internal readonly struct SessionRequest
    {
        public long? Id { get; init; }

        public string Op { get; init; }

        public JsonElement Body { get; init; }

        public bool HasBody => Body.ValueKind != JsonValueKind.Undefined;


        // Reads the envelope fields this version knows and ignores every other, which is
        // what lets version 1 grow. False when the frame is not a JSON object.
        public static bool TryRead(JsonDocument frame, out SessionRequest request)
        {
            request = default;

            var root = frame.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            long? id = null;

            if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out var parsedId))
                id = parsedId;

            var op = root.TryGetProperty("op", out var opElement) && opElement.ValueKind == JsonValueKind.String ? opElement.GetString() : null;

            root.TryGetProperty("body", out var body);

            request = new SessionRequest { Id = id, Op = op, Body = body };

            return true;
        }
    }
}
