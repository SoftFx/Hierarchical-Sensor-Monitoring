using System;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZstdSharp;

namespace HSMServer.Core.Tests.SensorApi
{
    // The acknowledgement of a request on a file connection, as a collector reads it (#1884).
    internal sealed record FileAck(long Id, int Status, JsonNode Body, string Error, int? RetryAfter, JsonNode Raw)
    {
        public string Upload => (string)Body?["upload"];

        public long? Next => (long?)Body?["next"];

        public long? Pieces => (long?)Body?["pieces"];

        public int? PieceBytes => (int?)Body?["pieceBytes"];

        public string State => (string)Body?["state"];

        public string Reason => (string)Body?["reason"];

        public static FileAck Parse(JsonNode ack) =>
            new((long)ack["ack"], (int)ack["status"], ack["body"], (string)ack["error"], (int?)ack["retryAfter"], ack);

        public override string ToString() => Raw.ToJsonString();
    }


    // One piece as a collector makes it: the original bytes compressed by zstd (level 3) into one frame,
    // with the header of the contract written out byte by byte. It is test code, not the collector's:
    // steps 5 and 6 build the real ones.
    internal sealed class TestPiece
    {
        public long Number { get; init; }

        public byte[] Original { get; init; }

        public byte[] Compressed { get; set; }

        public byte[] Digest { get; set; }

        public uint? Checksum { get; set; }

        public uint? DecompressedLength { get; set; }

        public uint? CompressedLength { get; set; }

        public string Upload { get; set; }


        public static TestPiece Of(long number, byte[] original)
        {
            using var compressor = new Compressor(3);

            return new TestPiece { Number = number, Original = original, Compressed = compressor.Wrap(original).ToArray(), Digest = SHA256.HashData(original) };
        }

        public byte[] Frame(long id, string upload)
        {
            var frame = new byte[88 + Compressed.Length];

            frame[0] = 1;
            frame[1] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), 88);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8), id);
            Convert.FromHexString(Upload ?? upload).CopyTo(frame.AsSpan(16));
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32), Number);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40), DecompressedLength ?? (uint)Original.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(44), CompressedLength ?? (uint)Compressed.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(48), Checksum ?? Crc32.HashToUInt32(Compressed));
            Digest.CopyTo(frame.AsSpan(56));
            Compressed.CopyTo(frame.AsSpan(88));

            return frame;
        }
    }


    // A file cut into pieces, with the hash of the whole file as the contract defines it: the SHA-256 of
    // the pieces' digests in order.
    internal sealed class TestFile
    {
        public string Name { get; init; }

        public byte[] Bytes { get; init; }

        public int PieceBytes { get; init; }

        public DateTime Modified { get; init; } = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

        public string Source { get; init; }

        public long PieceCount => (Bytes.Length + PieceBytes - 1) / PieceBytes;


        // Random bytes do not compress: every piece frame is larger than 64 KiB once a piece is.
        public static TestFile Random(string name, int size, int pieceBytes = 64 * 1024, int seed = 1)
        {
            var bytes = new byte[size];
            new Random(seed).NextBytes(bytes);

            return new TestFile { Name = name, Bytes = bytes, PieceBytes = pieceBytes };
        }

        public TestPiece Piece(long number)
        {
            var start = (int)(number * PieceBytes);

            return TestPiece.Of(number, Bytes.AsSpan(start, Math.Min(PieceBytes, Bytes.Length - start)).ToArray());
        }

        public string Hash()
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            for (var i = 0; i < PieceCount; i++)
                hash.AppendData(Piece(i).Digest);

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        public string Begin(long id, string path, int? pieceBytes = null) =>
            new JsonObject
            {
                ["id"] = id,
                ["op"] = "begin",
                ["body"] = new JsonObject
                {
                    ["path"] = path,
                    ["name"] = Name,
                    ["size"] = Bytes.Length,
                    ["modified"] = Modified.ToString("O"),
                    ["source"] = Source,
                    ["pieceBytes"] = pieceBytes ?? PieceBytes,
                },
            }.ToJsonString();

        public static string End(long id, string upload, string hash) =>
            new JsonObject { ["id"] = id, ["op"] = "end", ["body"] = new JsonObject { ["upload"] = upload, ["hash"] = hash } }.ToJsonString();
    }


    // The smallest client of a file connection (#1884) the tests need.
    internal sealed class FileConnectionTestClient : IDisposable
    {
        public const string Subprotocol = "hsm.files.v1";

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private long _nextId;


        public ClientWebSocket Socket { get; }

        public JsonNode Hello { get; private set; }

        // The session it belongs to, when the client holds it; disposed with the client.
        public ClientWebSocket Session { get; set; }


        private FileConnectionTestClient(ClientWebSocket socket) => Socket = socket;


        public static async Task<FileConnectionTestClient> OpenAsync(Uri sessionUri, string key, string sessionId)
        {
            var socket = TableConnectionTestClient.NewSocket(key, Subprotocol, sessionId);

            await socket.ConnectAsync(sessionUri, Deadline());

            var client = new FileConnectionTestClient(socket) { Hello = await TableConnectionTestClient.ReceiveAsync(socket) };

            Assert.Equal("hello", (string)client.Hello["op"]);

            return client;
        }

        public static async Task<HttpStatusCode> RefusedStatusAsync(Uri sessionUri, string key, string sessionId, params string[] subprotocols)
        {
            using var socket = TableConnectionTestClient.NewSocket(key, subprotocols.Length == 0 ? Subprotocol : subprotocols[0], sessionId);

            for (var i = 1; i < subprotocols.Length; i++)
                socket.Options.AddSubProtocol(subprotocols[i]);

            await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(sessionUri, Deadline()));

            return socket.HttpStatusCode;
        }


        public long NextId() => Interlocked.Increment(ref _nextId);

        public async Task<FileAck> BeginAsync(TestFile file, string path, int? pieceBytes = null)
        {
            await SendTextAsync(file.Begin(NextId(), path, pieceBytes));

            return await ReceiveAckAsync();
        }

        public async Task<FileAck> SendPieceAsync(TestPiece piece, string upload)
        {
            await SendBinaryAsync(piece.Frame(NextId(), upload));

            return await ReceiveAckAsync();
        }

        public async Task<FileAck> EndAsync(string upload, string hash)
        {
            await SendTextAsync(TestFile.End(NextId(), upload, hash));

            return await ReceiveAckAsync();
        }

        // Sends `end` until the check is no longer running; the waits between are the test's, not the
        // retryAfter of the answer.
        public async Task<FileAck> EndUntilSettledAsync(string upload, string hash)
        {
            var deadline = DateTime.UtcNow + Patience;

            while (true)
            {
                var ack = await EndAsync(upload, hash);

                if (ack.Status != 202 || DateTime.UtcNow > deadline)
                    return ack;

                await Task.Delay(20);
            }
        }

        // Begin, every piece from `next`, end until settled.
        public async Task<FileAck> UploadAsync(TestFile file, string path)
        {
            var begun = await BeginAsync(file, path);

            Assert.True(begun.Status == 200 && begun.Next is not null, $"begin: {begun}");

            for (var i = begun.Next.Value; i < file.PieceCount; i++)
                Assert.Equal(200, (await SendPieceAsync(file.Piece(i), begun.Upload)).Status);

            return await EndUntilSettledAsync(begun.Upload, file.Hash());
        }

        public Task SendBinaryAsync(byte[] frame) => Socket.SendAsync(frame, WebSocketMessageType.Binary, true, Deadline());

        public Task SendTextAsync(string frame) => Socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, Deadline());

        public async Task<FileAck> ReceiveAckAsync() => FileAck.Parse(await TableConnectionTestClient.ReceiveAsync(Socket));

        public Task<JsonNode> ReceiveAsync() => TableConnectionTestClient.ReceiveAsync(Socket);

        public Task<WebSocketCloseStatus?> ExpectCloseAsync() => TableConnectionTestClient.ExpectCloseAsync(Socket);

        public void Dispose()
        {
            Socket.Dispose();
            Session?.Dispose();
        }

        private static CancellationToken Deadline() => new CancellationTokenSource(Patience).Token;
    }
}
