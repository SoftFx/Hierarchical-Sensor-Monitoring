using Microsoft.AspNetCore.Http;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text.Json;

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
    internal static class SessionProtocol
    {
        public const string Subprotocol = "hsm.sensors.v1";

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

        private static readonly byte[] _pong = "{\"op\":\"pong\"}"u8.ToArray();

        private static readonly object _upgradedKey = new();

        public static ReadOnlyMemory<byte> Pong => _pong;


        // Set by the handler once a request became a session. The status code cannot tell:
        // an upgrade answers 101 over HTTP/1.1 and 200 over HTTP/2.
        public static void MarkUpgraded(HttpContext context) => context.Items[_upgradedKey] = true;

        public static bool WasUpgraded(HttpContext context) => context.Items.ContainsKey(_upgradedKey);


        public static void WriteHello(IBufferWriter<byte> output, int maxFrameBytes, int heartbeatSeconds)
        {
            using var writer = new Utf8JsonWriter(output);

            writer.WriteStartObject();
            writer.WriteString(_op, OpHello);
            writer.WriteStartObject(_body);
            writer.WriteNumber(_maxFrameBytes, maxFrameBytes);
            writer.WriteNumber(_heartbeatSeconds, heartbeatSeconds);
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
