using HSMCommon.TaskResult;
using HSMDatabase.AccessManager.TableBlocks;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.TableSensors;
using HSMServer.Model.Authentication;
using HSMServer.SensorApi;
using HSMServer.ServerConfiguration;
using HSMTableBlocks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using NLog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.TablePages
{
    /// <summary>The body of <c>POST /TableSensors/Chart</c>.</summary>
    public sealed class TableChartRequest
    {
        /// <summary>The sensor's id, as the tree's encoded id.</summary>
        public string Id { get; set; }

        /// <summary>The range <c>[From, To)</c>; a time without a kind is UTC.</summary>
        public DateTime From { get; set; }

        public DateTime To { get; set; }

        /// <summary>Ordinals of value columns; null for every double column, empty for the row counts only.</summary>
        public int[] Columns { get; set; }

        /// <summary>The width of the plot in pixels; clamped to <see cref="TablePageService.MinBuckets"/>..<see cref="TablePageService.MaxBuckets"/>.</summary>
        public int Buckets { get; set; }
    }


    /// <summary>
    /// What the web UI reads of a table sensor's rows (#1847): the chart of a range and the download of a range as CSV. The
    /// controller only hands requests over; this service checks access, bounds the request, takes a turn of the shared
    /// <see cref="TableReadGate"/> for each read, and turns results into HTTP answers.
    /// <para>Access: the sensor is looked up by id in the tree and must belong to a product the user may see
    /// (<c>User.IsProductAvailable</c>: admins see every product). An id the user may not see is answered exactly as an
    /// unknown id (404, the same text), so the routes do not tell an outsider that a sensor exists. A visible sensor that
    /// is not a table sensor is a 400. No access key takes part: the browser never holds one.</para>
    /// </summary>
    public sealed class TablePageService
    {
        public const int MinBuckets = 100;
        public const int MaxBuckets = 2000;

        public const string NotFoundText = "No table sensor with this id.";

        // Retry-After of a 503: the block store cannot be read now (the read route's value).
        public const int RetryAfterSeconds = TableReadService.RetryAfterSeconds;

        // The largest magnitude a JavaScript number holds exactly.
        internal const long MaxExactInteger = 1L << 53;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ITreeValuesCache _cache;
        private readonly ITableBlockService _blocks;
        private readonly ITableChartReader _charts;
        private readonly TableReadGate _reads;
        private readonly int _pageRows;
        private readonly int _maxReadRows;
        private readonly long _maxDownloadRows;


        public TablePageService(ITreeValuesCache cache, ITableBlockService blocks, ITableChartReader charts, TableReadGate reads, IServerConfig config)
        {
            var tables = config.TableSensors ?? new TableSensorsConfig();

            _cache = cache;
            _blocks = blocks;
            _charts = charts;
            _reads = reads;
            _maxReadRows = tables.MaxReadRows;
            _pageRows = Math.Min(tables.MaxTextReadRows, tables.MaxReadRows);
            _maxDownloadRows = tables.MaxDownloadRows;
        }


        // === The chart ===

        public async Task<IActionResult> ChartAsync(HttpContext context, User user, TableChartRequest request)
        {
            var token = context.RequestAborted;
            NoStore(context);

            if (request is null)
                return Refuse(StatusCodes.Status400BadRequest, "The body cannot be read as a chart request.");

            if (Resolve(user, request.Id, out var table) is { } refused)
                return refused;

            var from = Utc(request.From);
            var to = Utc(request.To);

            if (from >= to)
                return Refuse(StatusCodes.Status400BadRequest, "From must be before To: the range is [From, To).");

            var buckets = Math.Clamp(request.Buckets, MinBuckets, MaxBuckets);
            var columns = request.Columns ?? DoubleColumns(table.Schema);

            TaskResult<TableChart> result;

            try
            {
                await _reads.WaitAsync(token);

                try
                {
                    // Off the request's thread: the read decompresses blocks.
                    result = await Task.Run(() => _charts.Reduce(table.Id, from, to, columns, buckets, token), token);
                }
                finally
                {
                    _reads.Release();
                }
            }
            catch (TableBlockStoreException ex)
            {
                _logger.Debug($"The chart of table sensor {table.Id} is answered 503: {ex.Message}");
                return StoreFault();
            }
            catch (TableReadChangedException ex)
            {
                // The merge committed hours under each plan of the read: not a fault, the same request soon is answered.
                _logger.Info(ex.Message);
                return new StoreChangedResult();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The page went away; nobody waits for the answer.
                return new EmptyResult();
            }

            if (!result.IsOk)
                return Refuse(StatusCodes.Status400BadRequest, result.Error);

            return new JsonResult(ToJson(result.Value), JsonOptions);
        }

        // The answer of the chart. Times are UTC DateTimes, written as ISO 8601 with a Z, as the other chart endpoints of
        // the UI write them. A bucket without a value is null. An int64 beyond 2^53 is a string, and its column says so.
        internal static object ToJson(TableChart chart)
        {
            var rows = chart.Rows;
            var isRows = rows is not null;

            IEnumerable<DateTime> times = isRows
                ? rows.Time.Select(Time)
                : Enumerable.Range(0, chart.BucketCount).Select(b => Time(chart.FromTicks + b * chart.BucketTicks));

            return new
            {
                mode = isRows ? "rows" : "buckets",
                from = Time(chart.FromTicks),
                to = Time(chart.ToTicks),
                bucketTicks = chart.BucketTicks,
                bucketCount = chart.BucketCount,
                times = times.ToArray(),
                bucketRows = isRows ? null : chart.BucketRows,
                rowCount = chart.RowCount,
                completeTo = chart.CompleteToTicks is { } complete ? Time(complete) : (DateTime?)null,
                blocksFailed = chart.BlocksFailed,
                batchesFailed = chart.BatchesFailed,
                columns = chart.Columns.Select(column => isRows ? RowsColumn(column, rows) : BucketsColumn(column)).ToArray(),
            };
        }

        private static object BucketsColumn(TableChartColumn column)
        {
            var count = column.ValueCount.Length;
            var min = new object[count];
            var max = new object[count];
            var approximate = false;

            for (int b = 0; b < count; b++)
            {
                if (column.ValueCount[b] == 0)
                    continue;

                if (column.Type == TableColumnType.Int64)
                {
                    min[b] = Integer(column.Int64Min[b], ref approximate);
                    max[b] = Integer(column.Int64Max[b], ref approximate);
                }
                else
                {
                    min[b] = column.DoubleMin[b];
                    max[b] = column.DoubleMax[b];
                }
            }

            return new
            {
                ordinal = column.Ordinal,
                name = column.Name,
                type = TableRowsText.TypeName(column.Type),
                approximate,
                min,
                max,
                values = (object[])null,
                nullCount = column.TotalNulls,
                nonFinite = column.TotalNonFinite,
            };
        }

        // The column's cells of the rows of a short range: null for a null cell and for NaN or an infinity (counted in
        // nonFinite), which a plot cannot draw.
        private static object RowsColumn(TableChartColumn column, TableRows rows)
        {
            var cells = rows.Columns[column.Ordinal];
            var values = new object[rows.RowCount];
            var approximate = false;

            for (int row = 0; row < values.Length; row++)
            {
                if (cells.IsNull(row))
                    continue;

                if (cells.Type == TableColumnType.Int64)
                    values[row] = Integer(cells.Int64Values[row], ref approximate);
                else if (double.IsFinite(cells.DoubleValues[row]))
                    values[row] = cells.DoubleValues[row];
            }

            return new
            {
                ordinal = column.Ordinal,
                name = column.Name,
                type = TableRowsText.TypeName(column.Type),
                approximate,
                min = (object[])null,
                max = (object[])null,
                values,
                nullCount = column.TotalNulls,
                nonFinite = column.TotalNonFinite,
            };
        }

        private static object Integer(long value, ref bool approximate)
        {
            if (value >= -MaxExactInteger && value <= MaxExactInteger)
                return value;

            approximate = true;
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static DateTime Time(long ticks) => new(ticks, DateTimeKind.Utc);

        private static int[] DoubleColumns(TableSchema schema) =>
            Enumerable.Range(1, schema.Columns.Count - 1).Where(c => schema.Columns[c].Type == TableColumnType.Double).ToArray();


        // === The download ===

        /// <summary>
        /// The rows of <c>[from, to)</c> as CSV, the form of the read route's CSV. The first page is read before anything
        /// is written, so what is known then is answered with a status and a text: failed blocks or batches (500), more
        /// rows than <c>TableSensors.MaxDownloadRows</c> (400), a store that cannot be read now (503). Past the first byte
        /// the same findings cut the connection off, so the browser shows a failed download and never a file that looks
        /// complete without being so.
        /// </summary>
        public async Task<IActionResult> DownloadAsync(HttpContext context, User user, string id, DateTime from, DateTime to)
        {
            var token = context.RequestAborted;
            NoStore(context);

            if (Resolve(user, id, out var table) is { } refused)
                return refused;

            var fromUtc = Utc(from);
            var toUtc = Utc(to);

            if (fromUtc >= toUtc)
                return Refuse(StatusCodes.Status400BadRequest, "From must be before To: the range is [From, To).");

            TableRows first;

            try
            {
                var (rows, error) = await ReadPageAsync(table.Id, fromUtc, toUtc, token);
                if (error is not null)
                    return Refuse(StatusCodes.Status400BadRequest, error);

                first = rows;
            }
            catch (TableBlockStoreException ex)
            {
                _logger.Debug($"A download of table sensor {table.Id} is answered 503: {ex.Message}");
                return StoreFault();
            }
            catch (TableReadChangedException ex)
            {
                // The merge committed hours under each plan of the read: the same request soon is answered.
                _logger.Info(ex.Message);
                return new StoreChangedResult();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new EmptyResult();
            }

            if (Incomplete(first) is { } damage)
                return Refuse(StatusCodes.Status500InternalServerError, $"The range cannot be downloaded whole: {damage}");

            if (first.RowsAtTimeOmitted)
                return Refuse(StatusCodes.Status400BadRequest, TooManyAtOneTime(first));

            if (first.RowCount > _maxDownloadRows)
                return Refuse(StatusCodes.Status400BadRequest, TooMany());

            return new TableDownloadResult(this, table.Id, toUtc, first, FileName(table.FullPath, fromUtc, toUtc));
        }

        internal long MaxDownloadRows => _maxDownloadRows;

        /// <summary>
        /// One page of a download from <paramref name="from"/>: at most the text page of the read route, read again at
        /// <c>MaxReadRows</c> when one time has more rows than the page (so no row of that time is skipped). An error text
        /// when the read refused the request (the sensor was removed meanwhile).
        /// </summary>
        internal async Task<(TableRows Rows, string Error)> ReadPageAsync(Guid sensorId, DateTime from, DateTime to, CancellationToken token)
        {
            var page = await ReadAsync(sensorId, from, to, _pageRows, token);

            if (page.IsOk && page.Value.RowsAtTimeOmitted && _pageRows < _maxReadRows)
                page = await ReadAsync(sensorId, from, to, _maxReadRows, token);

            return page.IsOk ? (page.Value, null) : (null, page.Error);
        }

        // One read, in a turn of the shared gate: a turn per page, so a client that reads slowly does not hold a turn.
        private async Task<TaskResult<TableRows>> ReadAsync(Guid sensorId, DateTime from, DateTime to, int maxRows, CancellationToken token)
        {
            await _reads.WaitAsync(token);

            try
            {
                return await Task.Run(() => _blocks.Read(sensorId, from, to, maxRows, token), token);
            }
            finally
            {
                _reads.Release();
            }
        }

        internal static string Incomplete(TableRows rows) =>
            rows.BlocksFailed > 0 || rows.BatchesFailed > 0
                ? $"{rows.BlocksFailed} stored block(s) and {rows.BatchesFailed} batch(es) of it cannot be read, and their rows would be missing."
                : null;

        internal string TooMany() => $"The range holds more than {_maxDownloadRows:N0} rows (TableSensors.MaxDownloadRows); download a shorter range.";

        internal string TooManyAtOneTime(TableRows rows) =>
            $"One time of the range holds more than {_maxReadRows:N0} rows (TableSensors.MaxReadRows), more than one read returns; " +
            $"they cannot be downloaded whole ({(rows.NextFromTicks is { } next ? $"the time {TableRowsText.Time(next - 1)}" : "the last time of the range")}).";

        // As ExportHistory names a history file, with the characters that are not safe in a file name replaced.
        internal static string FileName(string fullPath, DateTime from, DateTime to)
        {
            var name = $"{fullPath.Replace('/', '_')}_from_{from:s}_to{to:s}.csv";
            var safe = new StringBuilder(name.Length);

            foreach (var c in name)
                safe.Append(char.IsControl(c) || "\\/:*?\"<>|".IndexOf(c) >= 0 ? '_' : c);

            return safe.ToString();
        }


        // === Common ===

        // The sensor of the id, if the user may see it and it is a table sensor; otherwise the answer.
        private IActionResult Resolve(User user, string id, out TableSensorModel table)
        {
            table = null;

            var sensor = Guid.TryParse(id, out var sensorId) ? _cache.GetSensor(sensorId) : null;

            // Unknown and not visible are one answer.
            if (sensor is null || user is null || !user.IsProductAvailable(sensor.Root.Id))
                return Refuse(StatusCodes.Status404NotFound, NotFoundText);

            if (sensor is not TableSensorModel model)
                return Refuse(StatusCodes.Status400BadRequest, $"Sensor \"{sensor.FullPath}\" is not a table sensor.");

            table = model;
            return null;
        }

        private static IActionResult StoreFault() => new StoreFaultResult();

        // Every answer of both routes holds a product's data or says whether a sensor exists: no cache between the browser
        // and the server may keep one and hand it to another user.
        private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

        private static ContentResult Refuse(int status, string text) => new() { StatusCode = status, Content = text, ContentType = "text/plain; charset=utf-8" };

        private static DateTime Utc(DateTime time) => time.Kind switch
        {
            DateTimeKind.Local => time.ToUniversalTime(),
            _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        };


        // 503 with Retry-After of a second: the store changed under every plan of a chart or of the first page of a download
        // (TableReadChangedException).
        internal sealed class StoreChangedResult : IActionResult
        {
            public const string Text = "The table store changed during the read (hours were merged meanwhile); try again.";

            public Task ExecuteResultAsync(ActionContext context)
            {
                context.HttpContext.Response.Headers.RetryAfter = "1";
                return Refuse(StatusCodes.Status503ServiceUnavailable, Text).ExecuteResultAsync(context);
            }
        }

        // 503 with Retry-After: the block store cannot be read now; the same request later is answered.
        internal sealed class StoreFaultResult : IActionResult
        {
            public const string Text = "The table block store cannot be read now.";

            public Task ExecuteResultAsync(ActionContext context)
            {
                context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                return Refuse(StatusCodes.Status503ServiceUnavailable, Text).ExecuteResultAsync(context);
            }
        }
    }


    /// <summary>
    /// The CSV of a download, page by page: the first page was read before the headers, the next ones are read as the
    /// previous is written, one page in memory at a time, the header line once. A finding past the first byte (failed
    /// blocks or batches, more rows than allowed, one time with more rows than a read returns, a store fault, the sensor
    /// gone) aborts the connection and is logged.
    /// </summary>
    internal sealed class TableDownloadResult(TablePageService service, Guid sensorId, DateTime to, TableRows first, string fileName) : IActionResult
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        internal string FileName => fileName;

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var http = context.HttpContext;
            var response = http.Response;
            var token = http.RequestAborted;

            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(fileName);

            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = disposition.ToString();

            var page = first;
            var header = true;
            long written = 0;

            try
            {
                while (true)
                {
                    await TableRowsText.WriteCsvAsync(response.BodyWriter, page, token, header);
                    header = false;
                    written += page.RowCount;

                    if (page.NextFromTicks is not { } next)
                        break;

                    var (rows, error) = await service.ReadPageAsync(sensorId, new DateTime(next, DateTimeKind.Utc), to, token);

                    if (error is not null)
                    {
                        Abort(http, $"the read refused the next page: {error}", written);
                        return;
                    }

                    if (TablePageService.Incomplete(rows) is { } damage)
                    {
                        Abort(http, damage, written);
                        return;
                    }

                    if (rows.RowsAtTimeOmitted)
                    {
                        Abort(http, service.TooManyAtOneTime(rows), written);
                        return;
                    }

                    if (written + rows.RowCount > service.MaxDownloadRows)
                    {
                        Abort(http, service.TooMany(), written);
                        return;
                    }

                    page = rows;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The browser went away.
            }
            catch (TableBlockStoreException ex)
            {
                Abort(http, $"the block store cannot be read now: {ex.Message}", written);
            }
            catch (TableReadChangedException ex)
            {
                Abort(http, $"the table store changed under every plan of the read: {ex.Message}", written);
            }
        }

        private void Abort(HttpContext http, string reason, long written)
        {
            _logger.Warn($"A download of table sensor {sensorId} ({fileName}) is cut off after {written:N0} rows: {reason}");
            http.Abort();
        }
    }
}
