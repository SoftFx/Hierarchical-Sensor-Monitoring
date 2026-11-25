using HSMDatabase.AccessManager.TableBlocks;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.TableSensors;
using HSMServer.Extensions;
using HSMServer.Middleware;
using HSMServer.ServerConfiguration;
using HSMTableBlocks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NLog;
using System;
using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi
{
    // The read route of table sensors (#1845, ADR-0021 point 13): POST api/sensors/tableHistory. The
    // rows of one table sensor in [From, To), found by its path in the product of the access key, as
    // JSON, CSV or a sequence of blocks of that one table. The controller only hands the request over.
    //
    // A refusal is an HTTP status with a short text: 400 for a request that cannot be answered as it
    // is, 404 for a path with no sensor in the key's product, 406 for a key that may not read, 503
    // (with Retry-After) when the block store cannot be read now or the key could not be looked up. A read is cut at a row limit and at
    // a time boundary; the headers say whether it was cut and where the next page starts.
    public sealed class TableReadService
    {
        public const string RowCountHeader = "X-Hsm-Row-Count";
        public const string TruncatedHeader = "X-Hsm-Truncated";
        public const string NextFromHeader = "X-Hsm-Next-From";
        public const string RowsAtTimeOmittedHeader = "X-Hsm-Rows-At-Time-Omitted";
        public const string BlocksFailedHeader = "X-Hsm-Blocks-Failed";
        public const string BatchesFailedHeader = "X-Hsm-Batches-Failed";
        public const string TableColumnsHeader = "X-Hsm-Table-Columns";

        public const string BlocksContentType = "application/vnd.hsm.table-blocks";

        public const string KeyLookupFault = "The access key cannot be checked now.";

        // The Retry-After of a 503: a store that cannot be read now, or a fault while the key was looked up.
        public const int RetryAfterSeconds = 60;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly ITreeValuesCache _cache;
        private readonly ITableBlockService _blocks;
        private readonly int _maxReadRows;
        private readonly int _maxTextReadRows;
        private readonly SemaphoreSlim _reads;


        public TableReadService(ITreeValuesCache cache, ITableBlockService blocks, IServerConfig config)
        {
            var tables = config.TableSensors ?? new TableSensorsConfig();

            _cache = cache;
            _blocks = blocks;
            _maxReadRows = tables.MaxReadRows;
            _maxTextReadRows = Math.Min(tables.MaxTextReadRows, tables.MaxReadRows);
            _reads = new SemaphoreSlim(tables.MaxConcurrentReads, tables.MaxConcurrentReads);
        }


        public async Task<IActionResult> ReadAsync(HttpContext context, TableHistoryRequest request)
        {
            var token = context.RequestAborted;

            // The server failed while it looked the key up (#1756): a fault, not a key that may not read.
            // The telemetry middleware answers it before the route runs; this is for a request that
            // reaches the route anyway.
            if (context.HasPublicApiFault())
            {
                context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                return Refuse(StatusCodes.Status503ServiceUnavailable, KeyLookupFault);
            }

            // The key filter of the route judges the key after the action has run; a read is checked
            // before anything is read.
            if (!context.TryGetPublicApiInfo(out var caller))
                return Refuse(StatusCodes.Status406NotAcceptable, "An access key is required");

            if (!caller.Key.IsValid(KeyPermissions.CanReadSensorData, out var keyError))
                return Refuse(StatusCodes.Status406NotAcceptable, keyError);

            if (request is null)
                return Refuse(StatusCodes.Status400BadRequest, "The body cannot be read as a table history request.");

            var parts = request.Path?.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

            if (parts.Length == 0)
                return Refuse(StatusCodes.Status400BadRequest, "The request has no path.");

            if (!Enum.IsDefined(request.Format))
                return Refuse(StatusCodes.Status400BadRequest, $"The format {(int)request.Format} is unknown: 0 JSON, 1 CSV, 2 blocks.");

            var from = Utc(request.From);
            var to = Utc(request.To);

            if (from >= to)
                return Refuse(StatusCodes.Status400BadRequest, "From must be before To: the range is [From, To).");

            if (request.MaxRows is <= 0)
                return Refuse(StatusCodes.Status400BadRequest, "MaxRows must be positive.");

            if (request.Format == TableHistoryFormat.Blocks && request.MaxBlockFormatVersion is { } version && version < TableBlockFormat.FormatVersion)
                return Refuse(StatusCodes.Status400BadRequest, $"This server writes table block format {TableBlockFormat.FormatVersion}; the client reads up to {version}.");

            var path = string.Join('/', parts);

            if (!_cache.TryGetSensorByPath(caller.Product.Id, path, out var sensor))
                return Refuse(StatusCodes.Status404NotFound, $"No sensor \"{path}\" in the product of the key.");

            if (sensor is not TableSensorModel)
                return Refuse(StatusCodes.Status400BadRequest, $"Sensor \"{path}\" is not a table sensor.");

            var cap = request.Format == TableHistoryFormat.Blocks ? _maxReadRows : _maxTextReadRows;
            var limit = Math.Min(request.MaxRows ?? cap, cap);

            TableRows rows;

            try
            {
                await _reads.WaitAsync(token);

                try
                {
                    // Off the request's thread: the read decompresses blocks.
                    var result = await Task.Run(() => _blocks.Read(sensor.Id, from, to, limit, token), token);

                    if (!result.IsOk)
                        return Refuse(StatusCodes.Status404NotFound, result.Error);

                    rows = result.Value;
                }
                finally
                {
                    _reads.Release();
                }
            }
            catch (TableBlockStoreException ex)
            {
                _logger.Debug($"A table history read of \"{path}\" is answered 503: {ex.Message}");

                context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                return Refuse(StatusCodes.Status503ServiceUnavailable, "The table block store cannot be read now.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The client went away; the read stopped between blocks and nobody waits for an answer.
                _logger.Debug($"A table history read of \"{path}\" was cancelled by its client.");
                return new EmptyResult();
            }

            return new TableRowsResult(path, rows, request.Format);
        }

        private static ContentResult Refuse(int status, string text) => new() { StatusCode = status, Content = text, ContentType = "text/plain; charset=utf-8" };

        private static DateTime Utc(DateTime time) => time.Kind switch
        {
            DateTimeKind.Local => time.ToUniversalTime(),
            _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        };
    }


    // The rows of one read written as the request asked, straight to the response: the headers first
    // (the rows are known), then the body in pieces.
    internal sealed class TableRowsResult(string path, TableRows rows, TableHistoryFormat format) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            var token = context.HttpContext.RequestAborted;

            response.StatusCode = StatusCodes.Status200OK;
            response.Headers[TableReadService.RowCountHeader] = rows.RowCount.ToString(CultureInfo.InvariantCulture);
            response.Headers[TableReadService.TruncatedHeader] = rows.Truncated ? "true" : "false";
            // Rows missing from the answer, by the store that lost them: blocks not merged yet, batches of merged hours (#1850).
            response.Headers[TableReadService.BlocksFailedHeader] = rows.BlocksFailed.ToString(CultureInfo.InvariantCulture);
            response.Headers[TableReadService.BatchesFailedHeader] = rows.BatchesFailed.ToString(CultureInfo.InvariantCulture);

            if (rows.NextFromTicks is { } next)
                response.Headers[TableReadService.NextFromHeader] = TableRowsText.Time(next);

            if (rows.RowsAtTimeOmitted)
                response.Headers[TableReadService.RowsAtTimeOmittedHeader] = "true";

            switch (format)
            {
                case TableHistoryFormat.Csv:
                    response.ContentType = "text/csv; charset=utf-8";
                    await TableRowsText.WriteCsvAsync(response.BodyWriter, rows, token);
                    break;

                case TableHistoryFormat.Blocks:
                    response.ContentType = TableReadService.BlocksContentType;
                    response.Headers[TableReadService.TableColumnsHeader] = TableRowsText.ColumnsHeader(rows);
                    await TableRowsBlocks.WriteAsync(response.BodyWriter, path, rows, token);
                    break;

                default:
                    response.ContentType = "application/json; charset=utf-8";
                    await TableRowsText.WriteJsonAsync(response.BodyWriter, path, rows, token);
                    break;
            }
        }
    }


    // The text forms of table rows (#1845): JSON and CSV.
    internal static class TableRowsText
    {
        // Rows written between two flushes to the client.
        private const int RowsPerFlush = 4096;

        // ISO 8601 in UTC with all seven fraction digits: exact to the tick.
        public static string Time(long ticks) => new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

        public static string TypeName(TableColumnType type) => type switch
        {
            TableColumnType.Time => "time",
            TableColumnType.Int64 => "int64",
            _ => "double",
        };

        // A double that JSON numbers cannot carry, as text; null for a finite one.
        public static string NonFinite(double value) =>
            double.IsNaN(value) ? "NaN" : double.IsPositiveInfinity(value) ? "Infinity" : double.IsNegativeInfinity(value) ? "-Infinity" : null;

        // X-Hsm-Table-Columns: name;type,... with the names percent-encoded (header values are ASCII).
        public static string ColumnsHeader(TableRows rows) =>
            string.Join(",", rows.Columns.Select(column => $"{Uri.EscapeDataString(column.Name)};{(int)column.Type}"));

        public static async Task WriteJsonAsync(PipeWriter output, string path, TableRows rows, CancellationToken token)
        {
            using (var writer = new Utf8JsonWriter(output))
            {
                writer.WriteStartObject();
                writer.WriteString("path", path);

                writer.WriteStartArray("columns");
                foreach (var column in rows.Columns)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", column.Name);
                    writer.WriteString("type", TypeName(column.Type));
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteNumber("rowCount", rows.RowCount);
                writer.WriteBoolean("truncated", rows.Truncated);

                if (rows.NextFromTicks is { } next)
                    writer.WriteString("nextFrom", Time(next));
                else
                    writer.WriteNull("nextFrom");

                writer.WriteBoolean("rowsAtTimeOmitted", rows.RowsAtTimeOmitted);
                writer.WriteNumber("blocksFailed", rows.BlocksFailed);
                writer.WriteNumber("batchesFailed", rows.BatchesFailed);

                writer.WriteStartArray("rows");

                for (var row = 0; row < rows.RowCount; row++)
                {
                    writer.WriteStartArray();

                    foreach (var column in rows.Columns)
                        WriteCell(writer, column, row);

                    writer.WriteEndArray();

                    if ((row + 1) % RowsPerFlush == 0)
                    {
                        await writer.FlushAsync(token);
                        await output.FlushAsync(token);
                    }
                }

                writer.WriteEndArray();
                writer.WriteEndObject();

                await writer.FlushAsync(token);
            }

            await output.FlushAsync(token);
        }

        private static void WriteCell(Utf8JsonWriter writer, TableRowsColumn column, int row)
        {
            if (column.IsNull(row))
            {
                writer.WriteNullValue();
                return;
            }

            switch (column.Type)
            {
                case TableColumnType.Time:
                    writer.WriteStringValue(Time(column.Int64Values[row]));
                    break;

                case TableColumnType.Int64:
                    writer.WriteNumberValue(column.Int64Values[row]);
                    break;

                default:
                    var value = column.DoubleValues[row];

                    if (NonFinite(value) is { } text)
                        writer.WriteStringValue(text);
                    else
                        writer.WriteNumberValue(value);
                    break;
            }
        }

        // RFC 4180: CRLF line ends, a header line with the column names, a name quoted when it holds a comma, a quote or
        // a line break. A null cell is empty.
        public static async Task WriteCsvAsync(PipeWriter output, TableRows rows, CancellationToken token)
        {
            var text = new StringBuilder();

            text.AppendJoin(',', rows.Columns.Select(column => Quote(column.Name))).Append("\r\n");

            for (var row = 0; row < rows.RowCount; row++)
            {
                for (var c = 0; c < rows.Columns.Count; c++)
                {
                    if (c > 0)
                        text.Append(',');

                    var column = rows.Columns[c];

                    if (column.IsNull(row))
                        continue;

                    switch (column.Type)
                    {
                        case TableColumnType.Time:
                            text.Append(Time(column.Int64Values[row]));
                            break;

                        case TableColumnType.Int64:
                            text.Append(column.Int64Values[row].ToString(CultureInfo.InvariantCulture));
                            break;

                        default:
                            var value = column.DoubleValues[row];
                            text.Append(NonFinite(value) ?? value.ToString("R", CultureInfo.InvariantCulture));
                            break;
                    }
                }

                text.Append("\r\n");

                if ((row + 1) % RowsPerFlush == 0)
                {
                    await WriteAsync(output, text, token);
                    text.Clear();
                }
            }

            await WriteAsync(output, text, token);
        }

        private static async Task WriteAsync(PipeWriter output, StringBuilder text, CancellationToken token)
        {
            if (text.Length == 0)
                return;

            output.Write(Encoding.UTF8.GetBytes(text.ToString()));
            await output.FlushAsync(token);
        }

        private static string Quote(string name) =>
            name.IndexOfAny([',', '"', '\r', '\n']) < 0 ? name : $"\"{name.Replace("\"", "\"\"")}\"";
    }


    // The blocks answer (#1845): the rows as a sequence of table blocks of the one table, each written
    // with its null maps, one after another; a reader takes them apart by the length in each header.
    internal static class TableRowsBlocks
    {
        // Rows per block: a multiple of 8, so the null maps of a block are whole bytes of the result's maps.
        public const int RowsPerBlock = 65_536;

        // The answer is written with the format's maxima: the identifier is the sensor's path, which the registration
        // does not limit to the default identifier length.
        private static readonly TableBlockLimits AnswerLimits = new(TableBlockLimits.FormatMaxDecompressedBytes, int.MaxValue,
            int.MaxValue, TableBlockLimits.FormatMaxColumnsPerTable, TableBlockLimits.FormatMaxTableIdLength);

        public static async Task WriteAsync(PipeWriter output, string path, TableRows rows, CancellationToken token)
        {
            var id = Encoding.UTF8.GetBytes(path);

            using var writer = new TableBlockWriter(3, AnswerLimits);

            for (var start = 0; start < rows.RowCount; start += RowsPerBlock)
            {
                output.Write(Block(writer, id, rows, start, Math.Min(RowsPerBlock, rows.RowCount - start)));
                await output.FlushAsync(token);
            }
        }

        // Rows [start, start + count) as one block.
        private static byte[] Block(TableBlockWriter writer, byte[] id, TableRows rows, int start, int count)
        {
            writer.Reset();
            writer.BeginTable(id, rows.Schema.Id, count);

            foreach (var column in rows.Columns)
            {
                var nulls = column.NullMap is { } map
                    ? map.AsSpan(start / 8, TableNullMap.ByteLength(count))
                    : ReadOnlySpan<byte>.Empty;

                switch (column.Type)
                {
                    case TableColumnType.Time:
                        writer.AddTimeColumn(column.Int64Values.AsSpan(start, count), nulls);
                        break;

                    case TableColumnType.Int64:
                        writer.AddInt64Column(column.Int64Values.AsSpan(start, count), nulls);
                        break;

                    default:
                        writer.AddDoubleColumn(column.DoubleValues.AsSpan(start, count), nulls);
                        break;
                }
            }

            return writer.ToArray();
        }
    }
}
