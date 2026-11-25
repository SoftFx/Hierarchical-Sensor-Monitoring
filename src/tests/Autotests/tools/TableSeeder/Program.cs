using HSMTableBlocks;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TableSeeder
{
    // Seeds the rows of one table sensor for the Playwright specs (#1847). It sends one table block over the
    // collector session's table connection (aicontext/features/server/collector-session/feature.md, "Table connection"; the
    // hsm.tables.v1 connection), as a collector does, and waits for the server's acknowledgement. The table
    // sensor must already be registered (the spec registers it through the HTTP API): this tool registers
    // nothing. Its client is modelled on TableConnectionTestClient of HSMServer.Core.Tests.
    //
    // Exit code 0 when the block was stored (or was a duplicate of a stored one), 1 on any refusal or fault
    // with the reason on stderr, 2 on wrong arguments.
    internal static class Program
    {
        private const string SessionSubprotocol = "hsm.sensors.v1";
        private const string TableSubprotocol = "hsm.tables.v1";
        private const string ClientName = "autotests-table-seeder";

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

        private const string Usage =
            "Usage: TableSeeder --url <https://host:port> --key <access key> --path <sensor path> --rows <count>\n" +
            "                   [--from-minutes <minutes before now, default 30>] [--to-minutes <minutes before now, default 0>]\n" +
            "                   [--columns <double column names, default bid,ask>] [--null <column>:<row index>]...\n" +
            "                   [--insecure]   accept the server's certificate without checking it (a test server's self-signed one)\n" +
            "The rows are spread evenly over [now - from, now - to); the first column is the time, the others are doubles.";


        private static async Task<int> Main(string[] args)
        {
            Options options;

            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(Usage);
                return 2;
            }

            try
            {
                await SeedAsync(options);
                return 0;
            }
            catch (SeedException ex)
            {
                Console.Error.WriteLine($"TableSeeder: {ex.Message}");
                return 1;
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or System.Net.Http.HttpRequestException)
            {
                Console.Error.WriteLine($"TableSeeder: the connection to {options.Url} failed: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private static async Task SeedAsync(Options options)
        {
            var schema = new TableSchema(
                new[] { new TableSchemaColumn("time", TableColumnType.Time) }
                    .Concat(options.Columns.Select(name => new TableSchemaColumn(name, TableColumnType.Double))));

            var block = BuildBlock(options, schema);
            var sessionUri = SessionUri(options.Url);

            using var session = NewSocket(options, SessionSubprotocol, sessionId: null);
            await ConnectAsync(session, sessionUri, "the session");

            var hello = await ReceiveAsync(session);
            if ((string)hello["op"] != "hello")
                throw new SeedException($"the session did not say hello: {hello.ToJsonString()}");

            var sessionId = (string)hello["body"]?["sessionId"];

            using var table = NewSocket(options, TableSubprotocol, sessionId);
            await ConnectAsync(table, sessionUri, "the table connection");

            var tableHello = await ReceiveAsync(table);
            if ((string)tableHello["op"] != "hello")
                throw new SeedException($"the table connection did not say hello: {tableHello.ToJsonString()}");

            await table.SendAsync(Frame(1, Guid.NewGuid(), 1, block), WebSocketMessageType.Binary, true, Deadline());

            var ack = await ReceiveAsync(table);
            var status = (int?)ack["status"];
            var body = ack["body"];
            var outcome = (string)body?["outcome"];

            if (status != 200)
                throw new SeedException($"the block was refused with {status}: {(string)ack["error"] ?? ack.ToJsonString()}");

            if (body?["refused"] is JsonArray refused && refused.Count > 0)
            {
                var reasons = string.Join("; ", refused.Select(r => $"{(string)r["table"]}: {(string)r["reason"]}"));
                throw new SeedException($"the server refused the table: {reasons}");
            }

            if (outcome is not ("stored" or "duplicate"))
                throw new SeedException($"unexpected acknowledgement: {ack.ToJsonString()}");

            Console.WriteLine($"TableSeeder: {options.Rows} rows of \"{options.Path}\" {outcome} (schema 0x{schema.Id:X8}).");

            await CloseAsync(table);
            await CloseAsync(session);
        }

        // The rows: times spread evenly over the range; a bid of two slow waves with a fast ripple, an ask a spread above it.
        private static byte[] BuildBlock(Options options, TableSchema schema)
        {
            var now = DateTime.UtcNow;
            var from = now.AddMinutes(-options.FromMinutes);
            var to = now.AddMinutes(-options.ToMinutes);

            if (from >= to)
                throw new SeedException("--from-minutes must be larger than --to-minutes: the range is [now - from, now - to).");

            var rows = options.Rows;
            var step = (to - from).Ticks / Math.Max(rows, 1);
            var times = new long[rows];

            for (int i = 0; i < rows; i++)
                times[i] = from.Ticks + i * step;

            using var writer = new TableBlockWriter();

            writer.BeginTable(Encoding.UTF8.GetBytes(options.Path), schema.Id, rows);
            writer.AddTimeColumn(times);

            for (int c = 0; c < options.Columns.Count; c++)
            {
                var name = options.Columns[c];
                var values = new double[rows];
                var nulls = new byte[TableNullMap.ByteLength(rows)];

                for (int i = 0; i < rows; i++)
                    values[i] = Math.Round(1.08 + 0.002 * Math.Sin(2 * Math.PI * 2 * i / rows) + 0.0002 * Math.Sin(i / 7.0) + 0.0003 * c, 6);

                foreach (var row in options.Nulls.Where(n => n.Column == name).Select(n => n.Row))
                {
                    if (row < 0 || row >= rows)
                        throw new SeedException($"--null {name}:{row} is outside the {rows} rows.");

                    TableNullMap.SetNull(nulls, row);
                }

                writer.AddDoubleColumn(values, nulls);
            }

            return writer.ToArray();
        }

        // The frame of a table connection: version 1, kind, reserved, header length 40, request id, producer id,
        // producer sequence, then the block (aicontext/features/server/collector-session/feature.md, "Table connection").
        private static byte[] Frame(byte kind, Guid producer, long sequence, byte[] block)
        {
            const int HeaderLength = 40;
            var frame = new byte[HeaderLength + block.Length];

            frame[0] = 1;
            frame[1] = kind;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), HeaderLength);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8), 1);
            producer.TryWriteBytes(frame.AsSpan(16, 16));
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32), sequence);
            block.CopyTo(frame.AsSpan(HeaderLength));

            return frame;
        }

        private static Uri SessionUri(Uri server)
        {
            var builder = new UriBuilder(server)
            {
                Scheme = server.Scheme == Uri.UriSchemeHttp ? "ws" : "wss",
                Path = "/api/sensors/session",
            };

            return builder.Uri;
        }

        private static ClientWebSocket NewSocket(Options options, string subprotocol, string sessionId)
        {
            var socket = new ClientWebSocket();

            socket.Options.CollectHttpResponseDetails = true;
            socket.Options.SetRequestHeader("Key", options.Key);
            socket.Options.SetRequestHeader("ClientName", ClientName);
            socket.Options.SetRequestHeader("X-Collector-Kind", "managed");
            socket.Options.SetRequestHeader("X-Collector-Version", "autotests");

            if (sessionId is not null)
                socket.Options.SetRequestHeader("X-Hsm-Session", sessionId);

            if (options.Insecure)
                socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

            socket.Options.AddSubProtocol(subprotocol);

            return socket;
        }

        private static async Task ConnectAsync(ClientWebSocket socket, Uri uri, string what)
        {
            try
            {
                await socket.ConnectAsync(uri, Deadline());
            }
            catch (WebSocketException ex) when (socket.HttpStatusCode != 0)
            {
                throw new SeedException($"{what} was refused with HTTP {(int)socket.HttpStatusCode}: {ex.Message}");
            }
        }

        private static async Task<JsonNode> ReceiveAsync(ClientWebSocket socket)
        {
            var message = new ArrayBufferWriter<byte>();
            var deadline = Deadline();

            while (true)
            {
                var result = await socket.ReceiveAsync(message.GetMemory(4096), deadline);

                if (result.MessageType == WebSocketMessageType.Close)
                    throw new SeedException($"the server closed the connection: {socket.CloseStatus} {socket.CloseStatusDescription}");

                if (result.MessageType != WebSocketMessageType.Text)
                    throw new SeedException($"expected a text frame, the server sent {result.MessageType}");

                message.Advance(result.Count);

                if (result.EndOfMessage)
                    return JsonNode.Parse(Encoding.UTF8.GetString(message.WrittenSpan));
            }
        }

        private static async Task CloseAsync(ClientWebSocket socket)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "seeded", Deadline());
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // The rows are acknowledged; a close that fails changes nothing.
            }
        }

        private static CancellationToken Deadline() => new CancellationTokenSource(Patience).Token;


        private sealed class SeedException(string message) : Exception(message);


        private sealed class Options
        {
            public Uri Url { get; private set; }
            public string Key { get; private set; }
            public string Path { get; private set; }
            public int Rows { get; private set; } = -1;
            public double FromMinutes { get; private set; } = 30;
            public double ToMinutes { get; private set; }
            public List<string> Columns { get; private set; } = ["bid", "ask"];
            public List<(string Column, int Row)> Nulls { get; } = [];
            public bool Insecure { get; private set; }

            public static Options Parse(string[] args)
            {
                var options = new Options();

                for (int i = 0; i < args.Length; i++)
                {
                    var name = args[i];

                    if (name == "--insecure")
                    {
                        options.Insecure = true;
                        continue;
                    }

                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{name} needs a value.");

                    var value = args[++i];

                    switch (name)
                    {
                        case "--url":
                            options.Url = Uri.TryCreate(value, UriKind.Absolute, out var url) ? url : throw new ArgumentException($"--url {value} is not an absolute URL.");
                            break;
                        case "--key":
                            options.Key = value;
                            break;
                        case "--path":
                            options.Path = value;
                            break;
                        case "--rows":
                            options.Rows = int.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "--from-minutes":
                            options.FromMinutes = double.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "--to-minutes":
                            options.ToMinutes = double.Parse(value, CultureInfo.InvariantCulture);
                            break;
                        case "--columns":
                            options.Columns = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                            break;
                        case "--null":
                            var parts = value.Split(':');
                            if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var row))
                                throw new ArgumentException($"--null {value} is not <column>:<row index>.");
                            options.Nulls.Add((parts[0], row));
                            break;
                        default:
                            throw new ArgumentException($"Unknown argument {name}.");
                    }
                }

                if (options.Url is null || string.IsNullOrEmpty(options.Key) || string.IsNullOrEmpty(options.Path) || options.Rows < 1)
                    throw new ArgumentException("--url, --key, --path and --rows (at least 1) are required.");

                if (options.Nulls.Any(n => !options.Columns.Contains(n.Column)))
                    throw new ArgumentException("--null names a column that is not in --columns.");

                return options;
            }
        }
    }
}
