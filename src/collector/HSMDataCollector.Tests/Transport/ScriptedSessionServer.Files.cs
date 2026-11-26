#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Tests.LargeFiles;

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// The file connection of the scripted server (#1884, wire contract "File connection"): the upgrade with
    /// <c>hsm.files.v1</c> and <c>X-Hsm-Session</c>, the hello with <c>fileLimits</c>, the probe, and <c>begin</c>,
    /// pieces, <c>end</c> and <c>discard</c> answered by a <see cref="FakeFileStore"/> (the same semantics the uploader's
    /// unit tests use), whose script decides any answer ahead of the real one.
    /// </summary>
    public sealed partial class ScriptedSessionServer
    {
        public const string FilesSubprotocol = "hsm.files.v1";

        private readonly ConcurrentDictionary<int, TableSocket> _fileOpen = new ConcurrentDictionary<int, TableSocket>();
        private readonly ConcurrentQueue<int> _fileCloses = new ConcurrentQueue<int>();
        private readonly ConcurrentQueue<Dictionary<string, string>> _fileUpgrades = new ConcurrentQueue<Dictionary<string, string>>();
        private readonly ConcurrentDictionary<long, TaskCompletionSource<bool>> _fileUnanswered = new ConcurrentDictionary<long, TaskCompletionSource<bool>>();
        // File connections are not offered unless a test asks for them (AcceptFileUpgrades): a scripted server without them
        // stands for a server older than large files, which is what every session test before #1888 talks to.
        private int _fileRefuseStatus = 404;
        private int _fileConnections;
        private int _refusedFileUpgrades;

        /// <summary>What the file connections of this server store and answer.</summary>
        public FakeFileStore Files { get; } = new FakeFileStore();

        /// <summary>Every file upgrade from now on is refused with this HTTP status; sessions are not touched.</summary>
        public void RefuseFileUpgrades(int status) => Volatile.Write(ref _fileRefuseStatus, status);

        public void AcceptFileUpgrades() => Volatile.Write(ref _fileRefuseStatus, 0);

        /// <summary>File upgrades accepted so far.</summary>
        public int FileConnections => Volatile.Read(ref _fileConnections);

        public int RefusedFileUpgrades => Volatile.Read(ref _refusedFileUpgrades);

        public IReadOnlyList<Dictionary<string, string>> FileUpgradeHeaders => _fileUpgrades.ToArray();

        /// <summary>The close codes the collector sent on file connections, in order.</summary>
        public IReadOnlyList<int> FileCloses => _fileCloses.ToArray();

        /// <summary>Completes once this server has left request <paramref name="id"/> of a file connection without an answer by its script.</summary>
        public Task WhenFileRequestUnanswered(long id) =>
            _fileUnanswered.GetOrAdd(id, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

        private static bool IsFileUpgrade(HttpListenerContext context)
        {
            var offered = context.Request.Headers["Sec-WebSocket-Protocol"] ?? string.Empty;

            return offered.Split(',').Any(p => p.Trim() == FilesSubprotocol);
        }

        private void CloseFileConnectionOf(string sessionId)
        {
            foreach (var file in _fileOpen.Values.Where(t => t.SessionId == sessionId).ToArray())
                _ = file.CloseAsync(1000, "its session ended");
        }

        private async Task HandleFileUpgradeAsync(HttpListenerContext context, Dictionary<string, string> headers)
        {
            _fileUpgrades.Enqueue(headers);

            var refuse = Volatile.Read(ref _fileRefuseStatus);

            headers.TryGetValue("X-Hsm-Session", out var sessionId);

            if (refuse == 0 && (sessionId == null || !_liveSessions.ContainsKey(sessionId)))
                refuse = 409;

            if (refuse != 0)
            {
                Interlocked.Increment(ref _refusedFileUpgrades);
                context.Response.StatusCode = refuse;
                context.Response.Close();
                return;
            }

            var upgraded = await context.AcceptWebSocketAsync(FilesSubprotocol).ConfigureAwait(false);
            var index = Interlocked.Increment(ref _fileConnections);
            var file = new TableSocket(upgraded.WebSocket, index, sessionId, context);

            _fileOpen[index] = file;
            Files.Note("open");

            try
            {
                await ServeFileAsync(file).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
            finally
            {
                _fileOpen.TryRemove(index, out _);
                file.Socket.Dispose();
            }
        }

        private async Task ServeFileAsync(TableSocket file)
        {
            await file.SendTextAsync(Files.HelloJson).ConfigureAwait(false);

            var buffer = new ArraySegment<byte>(new byte[64 * 1024]);
            var socket = file.Socket;

            while (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseSent)
            {
                WebSocketReceiveResult result;
                byte[] message;

                using (var stream = new MemoryStream())
                {
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, _stop.Token).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _fileCloses.Enqueue((int)(result.CloseStatus ?? 0));

                            if (socket.State == WebSocketState.CloseReceived)
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);

                            return;
                        }

                        stream.Write(buffer.Array, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    message = stream.ToArray();
                }

                FileReplyText reply;
                long id;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(message);

                    if (text.Contains("\"op\":\"ping\""))
                    {
                        await file.SendTextAsync("{\"op\":\"pong\"}").ConfigureAwait(false);
                        continue;
                    }

                    id = System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("id").GetInt64();
                    reply = Files.Request(id, text);
                }
                else
                {
                    if (message.Length < 88 || message[0] != 1 || (message[1] != 1 && message[1] != 2))
                    {
                        await file.CloseAsync(1002, "a binary frame without a trusted header").ConfigureAwait(false);
                        return;
                    }

                    id = BitConverter.ToInt64(message, 8);
                    reply = Files.Piece(message);
                }

                switch (reply.Kind)
                {
                    case FileAnswer.AnswerKind.Status:
                        await file.SendTextAsync(reply.Json).ConfigureAwait(false);
                        break;

                    case FileAnswer.AnswerKind.Close:
                        await file.CloseAsync(reply.CloseCode, "scripted").ConfigureAwait(false);
                        return;

                    case FileAnswer.AnswerKind.Drop:
                        try { socket.Abort(); } catch (Exception) { }
                        DropConnection(file.Context);
                        return;

                    default:
                        WhenFileRequestUnanswered(id);
                        _fileUnanswered[id].TrySetResult(true);
                        break;
                }
            }
        }
    }
}
