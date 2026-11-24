using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    // The acknowledgement of a frame on a table connection, as a collector reads it (#1845).
    // `Refused` is null when the server says it does not know them ("refusedKnown": false).
    internal sealed record TableAck(long Id, int Status, string Outcome, int? Tables, IReadOnlyList<(int Index, string Table, string Reason)> Refused,
        string Error, int? RetryAfter, JsonNode Raw)
    {
        public static TableAck Parse(JsonNode ack)
        {
            var body = ack["body"];
            List<(int, string, string)> refused = null;

            if (body?["refused"] is JsonArray list)
            {
                refused = new List<(int, string, string)>();

                foreach (var entry in list)
                    refused.Add(((int)entry["index"], (string)entry["table"], (string)entry["reason"]));
            }
            else if (body is not null && body["outcome"] is not null)
            {
                // The list is left out only together with refusedKnown: false.
                Assert.False((bool)body["refusedKnown"], $"an ack without its refused tables must say refusedKnown: false: {ack.ToJsonString()}");
            }

            return new TableAck((long)ack["ack"], (int)ack["status"], (string)body?["outcome"], (int?)body?["tables"], refused,
                (string)ack["error"], (int?)ack["retryAfter"], ack);
        }
    }


    // The smallest client of a table connection (#1845) the tests need: the upgrade with its
    // headers, the binary frame of the contract written out byte by byte, text acks read back.
    // It is test code, not the collector's: steps 4 and 6 build the real ones.
    internal sealed class TableConnectionTestClient : IDisposable
    {
        public const string Subprotocol = "hsm.tables.v1";
        public const string SessionSubprotocol = "hsm.sensors.v1";
        public const string ClientName = "table-connection-tests";

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);


        public ClientWebSocket Socket { get; }

        public JsonNode Hello { get; private set; }

        // The session it belongs to, when the client holds it; disposed with the client.
        public ClientWebSocket Session { get; set; }


        private TableConnectionTestClient(ClientWebSocket socket) => Socket = socket;


        // An ordinary session, past its hello; the id a table connection names.
        public static async Task<(ClientWebSocket Socket, string SessionId)> OpenSessionAsync(Uri sessionUri, string key)
        {
            var socket = NewSocket(key, SessionSubprotocol, sessionId: null);

            await socket.ConnectAsync(sessionUri, Deadline());

            var hello = await ReceiveAsync(socket);

            Assert.Equal("hello", (string)hello["op"]);

            return (socket, (string)hello["body"]["sessionId"]);
        }

        // A table connection, past its hello.
        public static async Task<TableConnectionTestClient> OpenAsync(Uri sessionUri, string key, string sessionId)
        {
            var socket = NewSocket(key, Subprotocol, sessionId);

            await socket.ConnectAsync(sessionUri, Deadline());

            var client = new TableConnectionTestClient(socket) { Hello = await ReceiveAsync(socket) };

            Assert.Equal("hello", (string)client.Hello["op"]);

            return client;
        }

        // The HTTP status of an upgrade the server refuses.
        public static async Task<HttpStatusCode> RefusedStatusAsync(Uri sessionUri, string key, string sessionId, string subprotocol = Subprotocol)
        {
            using var socket = NewSocket(key, subprotocol, sessionId);

            await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(sessionUri, Deadline()));

            return socket.HttpStatusCode;
        }

        public static ClientWebSocket NewSocket(string key, string subprotocol, string sessionId)
        {
            var socket = new ClientWebSocket();

            socket.Options.CollectHttpResponseDetails = true;
            socket.Options.SetRequestHeader("ClientName", ClientName);
            socket.Options.SetRequestHeader("X-Collector-Kind", "managed");
            socket.Options.SetRequestHeader("X-Collector-Version", "test");

            if (key is not null)
                socket.Options.SetRequestHeader("Key", key);

            if (sessionId is not null)
                socket.Options.SetRequestHeader("X-Hsm-Session", sessionId);

            socket.Options.AddSubProtocol(subprotocol);

            return socket;
        }


        // The frame of the contract: version 1, kind, reserved, header length 40, request id,
        // producer id, producer sequence, then the block.
        public static byte[] Frame(byte kind, long id, Guid producer, long sequence, ReadOnlySpan<byte> block)
        {
            var frame = new byte[40 + block.Length];

            frame[0] = 1;
            frame[1] = kind;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), 40);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8), id);
            producer.TryWriteBytes(frame.AsSpan(16, 16));
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32), sequence);
            block.CopyTo(frame.AsSpan(40));

            return frame;
        }

        public Task SendBlockAsync(long id, Guid producer, long sequence, byte[] block) => SendBinaryAsync(Frame(1, id, producer, sequence, block));

        public Task SendProbeAsync(long id) => SendBinaryAsync(Frame(2, id, Guid.Empty, 0, ReadOnlySpan<byte>.Empty));

        public Task SendBinaryAsync(byte[] frame) => Socket.SendAsync(frame, WebSocketMessageType.Binary, true, Deadline());

        public Task SendTextAsync(string frame) => Socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, Deadline());

        public async Task<TableAck> ReceiveAckAsync() => TableAck.Parse(await ReceiveAsync(Socket));

        public Task<JsonNode> ReceiveAsync() => ReceiveAsync(Socket);

        public Task<WebSocketCloseStatus?> ExpectCloseAsync() => ExpectCloseAsync(Socket);

        public void Dispose()
        {
            Socket.Dispose();
            Session?.Dispose();
        }


        public static async Task<JsonNode> ReceiveAsync(ClientWebSocket socket)
        {
            var message = new ArrayBufferWriter<byte>();
            var deadline = Deadline();

            while (true)
            {
                var result = await socket.ReceiveAsync(message.GetMemory(4096), deadline);

                Assert.True(result.MessageType == WebSocketMessageType.Text,
                    $"expected a text frame, the server answered {result.MessageType} ({socket.CloseStatus}: {socket.CloseStatusDescription})");

                message.Advance(result.Count);

                if (result.EndOfMessage)
                    return JsonNode.Parse(Encoding.UTF8.GetString(message.WrittenSpan));
            }
        }

        // Reads until the server's close frame and completes the handshake.
        public static async Task<WebSocketCloseStatus?> ExpectCloseAsync(ClientWebSocket socket)
        {
            var buffer = new byte[4096];
            var deadline = Deadline();

            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), deadline);

                if (result.MessageType != WebSocketMessageType.Close)
                    continue;

                // The answer to the server's close; a server that has let the connection go
                // already makes it fail, which does not change the close it sent.
                try
                {
                    if (socket.State == WebSocketState.CloseReceived)
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, deadline);
                }
                catch (WebSocketException)
                {
                }

                return socket.CloseStatus;
            }
        }

        private static CancellationToken Deadline() => new CancellationTokenSource(Patience).Token;
    }
}
