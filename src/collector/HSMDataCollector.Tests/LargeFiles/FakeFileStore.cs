#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using ZstdSharp;

namespace HSMDataCollector.Tests.LargeFiles
{
    /// <summary>How the fake server answers one request, when a test scripts it.</summary>
    public sealed class FileAnswer
    {
        public enum AnswerKind { Status, Drop, Close, Silent }

        public AnswerKind Kind { get; private set; }

        public int Status { get; private set; }

        public string Body { get; private set; }

        public int? RetryAfter { get; private set; }

        /// <summary>Whether the request is applied to the store before the scripted answer (a lost answer of a stored piece).</summary>
        public bool ApplyFirst { get; private set; }

        /// <summary>A 400 that discards the upload: the part is removed, as the real server removes it.</summary>
        public bool DiscardsPart { get; private set; }

        public static FileAnswer Discarded() => new FileAnswer { Kind = AnswerKind.Status, Status = 400, DiscardsPart = true };

        public static FileAnswer With(int status, string body = null, int? retryAfter = null) => new FileAnswer { Kind = AnswerKind.Status, Status = status, Body = body, RetryAfter = retryAfter };

        /// <summary>The connection is cut without a close handshake; with <paramref name="applied"/> the request was applied first.</summary>
        public static FileAnswer Drop(bool applied = false) => new FileAnswer { Kind = AnswerKind.Drop, ApplyFirst = applied };

        /// <summary>The server closes the connection with this code instead of answering.</summary>
        public static FileAnswer Close(int code) => new FileAnswer { Kind = AnswerKind.Close, Status = code };

        /// <summary>No answer at all; the request is not applied.</summary>
        public static FileAnswer Silent() => new FileAnswer { Kind = AnswerKind.Silent };

        public static FileAnswer Stored() => With(200, "{\"upload\":\"00000000000000000000000000000000\",\"state\":\"stored\",\"time\":\"2026-11-25T12:00:00.0000000Z\"}");
        public static FileAnswer Checking(int retryAfter = 1) => With(202, "{\"upload\":\"00000000000000000000000000000000\",\"state\":\"checking\"}", retryAfter);
        public static FileAnswer NoRoom(int retryAfter = 1) => With(503, null, retryAfter);
        public static FileAnswer Busy(int retryAfter = 1) => With(409, null, retryAfter);
        public static FileAnswer TooLarge() => With(413);
        public static FileAnswer CannotTake() => With(422);
        public static FileAnswer Invalid() => With(400);
    }


    /// <summary>One answer the fake server gave, as a channel sends it.</summary>
    public sealed class FileReplyText
    {
        public FileAnswer.AnswerKind Kind;
        public int CloseCode;
        public string Json;
    }


    /// <summary>
    /// The server side of the file connection as the wire contract describes it ("File connection"), in memory: parts by the
    /// file's sensor, name, size, modified and source; begin, pieces checked by their header, CRC-32, zstd frame and digest;
    /// end checks the hash of the digests; discard. A test scripts answers per op ahead of the real ones.
    /// </summary>
    public sealed class FakeFileStore
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, Part> _parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        private readonly Dictionary<string, Part> _byUpload = new Dictionary<string, Part>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<FileAnswer> _begin = new ConcurrentQueue<FileAnswer>();
        private readonly ConcurrentQueue<FileAnswer> _piece = new ConcurrentQueue<FileAnswer>();
        private readonly ConcurrentQueue<FileAnswer> _end = new ConcurrentQueue<FileAnswer>();
        private readonly ConcurrentQueue<FileAnswer> _discard = new ConcurrentQueue<FileAnswer>();
        private readonly Dictionary<string, (byte[] Content, long Pieces)> _held = new Dictionary<string, (byte[], long)>(StringComparer.Ordinal);
        private int _uploads;

        public FakeFileStore(int pieceBytes = 65536, long maxFileBytes = 64L * 1024 * 1024 * 1024)
        {
            PieceBytes = pieceBytes;
            MaxFileBytes = maxFileBytes;
        }

        public int PieceBytes { get; set; }

        public long MaxFileBytes { get; set; }

        public int HeartbeatSeconds { get; set; } = 30;

        /// <summary>The frame limit the hello states: the piece and its worst-case compression fit it.</summary>
        public int MaxFrameBytes => 88 + Compressor.GetCompressBound(PieceBytes) + 1024;

        /// <summary>Every request in order: "begin name", "piece n", "end", "discard", "probe".</summary>
        public List<string> Log { get; } = new List<string>();

        /// <summary>The piece numbers received in piece frames, in order of arrival.</summary>
        public List<long> PieceNumbers { get; } = new List<long>();

        /// <summary>The begin bodies received, in order.</summary>
        public List<JsonElement> Begins { get; } = new List<JsonElement>();

        public int Discards { get; private set; }

        public string HelloJson =>
            $"{{\"op\":\"hello\",\"body\":{{\"maxFrameBytes\":{MaxFrameBytes},\"heartbeatSeconds\":{HeartbeatSeconds},\"fileLimits\":{{\"maxFileBytes\":{MaxFileBytes},\"pieceBytes\":{PieceBytes}}}}}}}";

        public void Note(string entry)
        {
            lock (_lock)
                Log.Add(entry);
        }

        /// <summary>A copy of the log, taken under its lock.</summary>
        public List<string> LogSnapshot()
        {
            lock (_lock)
                return Log.ToList();
        }

        public List<long> PieceNumbersSnapshot()
        {
            lock (_lock)
                return PieceNumbers.ToList();
        }

        public void AnswerBeginNext(FileAnswer answer) => _begin.Enqueue(answer);
        public void AnswerPieceNext(FileAnswer answer) => _piece.Enqueue(answer);
        public void AnswerEndNext(FileAnswer answer) => _end.Enqueue(answer);
        public void AnswerDiscardNext(FileAnswer answer) => _discard.Enqueue(answer);

        /// <summary>The server already holds the first <paramref name="pieces"/> pieces of the file named <paramref name="name"/> (a continue).</summary>
        public void Holds(string name, byte[] content, long pieces)
        {
            lock (_lock)
                _held[name] = (content, pieces);
        }

        /// <summary>The stored (verified) file of this name, reassembled; null when none.</summary>
        public byte[] StoredFile(string name)
        {
            lock (_lock)
            {
                var part = _parts.Values.LastOrDefault(p => p.Name == name && p.Stored);
                return part?.Content();
            }
        }

        public int StoredCount(string name)
        {
            lock (_lock)
                return _parts.Values.Count(p => p.Name == name && p.Stored);
        }

        public int BeginCount(string name)
        {
            lock (_lock)
                return Log.Count(l => l == "begin " + name);
        }

        // ---- requests ----

        public FileReplyText Request(long id, string text)
        {
            using (var document = JsonDocument.Parse(text))
            {
                var root = document.RootElement;
                var op = root.GetProperty("op").GetString();
                var body = root.GetProperty("body").Clone();

                lock (_lock)
                {
                    switch (op)
                    {
                        case "begin":
                            Log.Add("begin " + body.GetProperty("name").GetString());
                            Begins.Add(body);
                            return Scripted(_begin, id, () => Begin(id, body));

                        case "end":
                            Log.Add("end");
                            return Scripted(_end, id, () => End(id, body));

                        case "discard":
                            Log.Add("discard");
                            return Scripted(_discard, id, () => Discard(id, body));

                        default:
                            return Json(id, 400, null, "unknown op");
                    }
                }
            }
        }

        public FileReplyText Piece(byte[] frame)
        {
            var id = BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(8));

            lock (_lock)
            {
                if (frame[1] == 2)
                {
                    Log.Add("probe");
                    return Json(id, 200, "{}");
                }

                var number = BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(32));

                Log.Add("piece " + number);
                PieceNumbers.Add(number);

                var reply = Scripted(_piece, id, () => StorePiece(id, frame));

                AfterPiece?.Invoke(number);
                return reply;
            }
        }

        /// <summary>Called with the number of every piece once it is answered (or left unanswered).</summary>
        public Action<long> AfterPiece { get; set; }

        private FileReplyText Scripted(ConcurrentQueue<FileAnswer> script, long id, Func<FileReplyText> real)
        {
            if (!script.TryDequeue(out var answer) || answer == null)
                return real();

            switch (answer.Kind)
            {
                case FileAnswer.AnswerKind.Status:
                    if (answer.DiscardsPart && Current != null)
                    {
                        Remove(Current);
                        return Json(id, 400, null, "scripted: the upload is discarded");
                    }

                    return Json(id, answer.Status, answer.Body, answer.Status >= 400 ? "scripted" : null, answer.RetryAfter);

                case FileAnswer.AnswerKind.Drop:
                    if (answer.ApplyFirst)
                        real();

                    return new FileReplyText { Kind = FileAnswer.AnswerKind.Drop };

                case FileAnswer.AnswerKind.Close:
                    return new FileReplyText { Kind = FileAnswer.AnswerKind.Close, CloseCode = answer.Status };

                default:
                    return new FileReplyText { Kind = FileAnswer.AnswerKind.Silent };
            }
        }

        private FileReplyText Begin(long id, JsonElement body)
        {
            var path = body.GetProperty("path").GetString();
            var name = body.GetProperty("name").GetString();
            var size = body.GetProperty("size").GetInt64();
            var modified = body.GetProperty("modified").GetString();
            var source = body.TryGetProperty("source", out var s) ? s.GetString() : null;
            var pieceBytes = body.GetProperty("pieceBytes").GetInt32();

            if (size > MaxFileBytes)
                return Json(id, 413, null, "too large");

            if (pieceBytes < 65536 || pieceBytes > PieceBytes)
                return Json(id, 400, null, "piece size out of range");

            var key = $"{path}|{name}|{size}|{modified}|{source}";

            if (_parts.TryGetValue(key, out var part))
            {
                if (part.Stored)
                    return Json(id, 200, $"{{\"upload\":\"{part.Upload}\",\"state\":\"stored\",\"time\":\"2026-11-25T12:00:00.0000000Z\"}}");

                if (part.PieceBytes != pieceBytes)
                    part.Restart(pieceBytes);

                Current = part;
                return Json(id, 200, $"{{\"upload\":\"{part.Upload}\",\"next\":{part.Next},\"pieces\":{part.PieceCount},\"pieceBytes\":{part.PieceBytes}}}");
            }

            part = new Part(path, name, size, ++_uploads, pieceBytes);

            if (_held.TryGetValue(name, out var held))
                part.Preload(held.Content, held.Pieces);

            _parts[key] = part;
            _byUpload[part.Upload] = part;
            Current = part;

            return Json(id, 200, $"{{\"upload\":\"{part.Upload}\",\"next\":{part.Next},\"pieces\":{part.PieceCount},\"pieceBytes\":{part.PieceBytes}}}");
        }

        /// <summary>The upload the connection holds now.</summary>
        public Part Current { get; private set; }

        private FileReplyText StorePiece(long id, byte[] frame)
        {
            var upload = Hex(frame.AsSpan(16, 16).ToArray());
            var number = BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(32));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(40));
            var compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(44));
            var crc = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(48));
            var digest = frame.AsSpan(56, 32).ToArray();
            var compressed = frame.AsSpan(88).ToArray();

            if (Current == null || Current.Upload != upload)
                return Json(id, 404, null, "no such upload on this connection");

            var part = Current;

            if (number < 0 || number >= part.PieceCount || compressedLength != compressed.Length || length != part.LengthOf(number))
                return Discard(part, id, "the piece breaks its bounds: the upload is discarded");

            if (Crc32.HashToUInt32(compressed) != crc)
                return Json(id, 409, $"{{\"next\":{part.Next},\"reason\":\"checksum mismatch\"}}");

            byte[] data;

            try
            {
                using (var decompressor = new Decompressor())
                    data = decompressor.Unwrap(compressed).ToArray();
            }
            catch (Exception)
            {
                return Discard(part, id, "the piece does not decompress: the upload is discarded");
            }

            if (data.Length != length)
                return Discard(part, id, "the piece decompresses to another length: the upload is discarded");

            if (number < part.Next)
                return Json(id, 200, $"{{\"next\":{part.Next}}}");

            if (number > part.Next)
                return Json(id, 409, $"{{\"next\":{part.Next},\"reason\":\"out of order\"}}");

            part.Pieces[number] = data;
            part.Digests[number] = digest;
            part.Next = number + 1;

            return Json(id, 200, $"{{\"next\":{part.Next}}}");
        }

        private FileReplyText End(long id, JsonElement body)
        {
            var upload = body.GetProperty("upload").GetString();
            var hash = body.GetProperty("hash").GetString();

            if (Current == null || Current.Upload != upload)
                return Json(id, 404, null, "no such upload on this connection");

            var part = Current;

            if (part.Next < part.PieceCount)
                return Json(id, 409, $"{{\"upload\":\"{upload}\",\"next\":{part.Next},\"reason\":\"pieces are missing\"}}");

            using (var sha = SHA256.Create())
            {
                for (long n = 0; n < part.PieceCount; n++)
                    if (!sha.ComputeHash(part.Pieces[n]).SequenceEqual(part.Digests[n]))
                        return Discard(part, id, $"piece {n}: its bytes do not match its digest: the upload is discarded");

                var expected = Hex(sha.ComputeHash(part.Digests.Take((int)part.PieceCount).SelectMany(d => d).ToArray()));

                if (expected != hash)
                    return Discard(part, id, "the hash does not match: the upload is discarded");
            }

            part.Stored = true;
            Current = null;

            return Json(id, 200, $"{{\"upload\":\"{upload}\",\"state\":\"stored\",\"time\":\"2026-11-25T12:00:00.0000000Z\"}}");
        }

        private FileReplyText Discard(long id, JsonElement body)
        {
            var upload = body.GetProperty("upload").GetString();

            if (Current == null || Current.Upload != upload)
                return Json(id, 404, null, "no such upload on this connection");

            Remove(Current);
            Discards++;

            return Json(id, 200, $"{{\"upload\":\"{upload}\",\"state\":\"discarded\"}}");
        }

        private FileReplyText Discard(Part part, long id, string error)
        {
            Remove(part);
            return Json(id, 400, null, error);
        }

        private void Remove(Part part)
        {
            foreach (var key in _parts.Where(p => p.Value == part).Select(p => p.Key).ToList())
                _parts.Remove(key);

            _byUpload.Remove(part.Upload);

            if (Current == part)
                Current = null;
        }

        private static FileReplyText Json(long id, int status, string body, string error = null, int? retryAfter = null)
        {
            var text = $"{{\"ack\":{id},\"status\":{status}";

            if (body != null)
                text += $",\"body\":{body}";

            if (error != null)
                text += $",\"error\":{JsonSerializer.Serialize(error)}";

            if (retryAfter.HasValue)
                text += $",\"retryAfter\":{retryAfter.Value}";

            return new FileReplyText { Kind = FileAnswer.AnswerKind.Status, Json = text + "}" };
        }

        internal static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));


        /// <summary>A part on the fake server.</summary>
        public sealed class Part
        {
            internal Part(string path, string name, long size, int number, int pieceBytes)
            {
                Path = path;
                Name = name;
                Size = size;
                Upload = number.ToString("x32");
                Restart(pieceBytes);
            }

            public string Path { get; }
            public string Name { get; }
            public long Size { get; }
            public string Upload { get; }
            public int PieceBytes { get; private set; }
            public long PieceCount { get; private set; }
            public long Next { get; internal set; }
            public bool Stored { get; internal set; }
            internal byte[][] Pieces;
            internal byte[][] Digests;

            internal void Restart(int pieceBytes)
            {
                PieceBytes = pieceBytes;
                PieceCount = (Size + pieceBytes - 1) / pieceBytes;
                Pieces = new byte[PieceCount][];
                Digests = new byte[PieceCount][];
                Next = 0;
            }

            internal long LengthOf(long number) => Math.Min(PieceBytes, Size - number * PieceBytes);

            internal void Preload(byte[] content, long pieces)
            {
                using (var sha = SHA256.Create())
                    for (long n = 0; n < pieces && n < PieceCount; n++)
                    {
                        Pieces[n] = content.Skip((int)(n * PieceBytes)).Take((int)LengthOf(n)).ToArray();
                        Digests[n] = sha.ComputeHash(Pieces[n]);
                        Next = n + 1;
                    }
            }

            internal byte[] Content() => Pieces.SelectMany(p => p ?? new byte[0]).ToArray();
        }
    }
}
