using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.LargeFiles;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;

namespace HSMServer.Core.Cache
{
    // The large values of File sensors (ADR-0022, #1883): the value row of a verified file, written on the product's queue
    // like any value, and the decisions of the store's pass that need the sensor and its rows. Internal: reached through
    // LargeFileUploadService only.
    public sealed partial class TreeValuesCache
    {
        // How many ticks after "now" a large value may move to find a free key in its sensor.
        private const int MaxValueTimeProbes = 1000;

        /// <summary>The clock a large value's time is taken from (the time the server accepted the file). Test seam.</summary>
        internal Func<DateTime> LargeFileClock { get; set; } = () => DateTime.UtcNow;


        /// <summary>
        /// The floor of a sensor's history at <paramref name="utcNow"/>: the later of its KeepHistory cutoff (none when
        /// the period is Forever or inherited as never expiring) and its last history clear. A row below it is removed by
        /// retention or was removed by a clear; a late value below it is not stored (#1441).
        /// </summary>
        internal static DateTime HistoryFloor(BaseSensorModel sensor, DateTime utcNow)
        {
            // Round-6 (#1441): KeepHistory inherited from a folder reaches here as a FromFolder policy carrying the folder
            // model's ticks, Forever as long.MaxValue and the month presets as 0. The retention pass (TimeIsUp) treats both
            // as "never expires", and the floor agrees.
            var keepPolicy = sensor.Settings.KeepHistory.Value;
            var keepFloor = keepPolicy.IsNone || (keepPolicy.UseTicks && (keepPolicy.Ticks <= 0L || keepPolicy.Ticks >= utcNow.Ticks))
                ? DateTime.MinValue
                : keepPolicy.GetShiftedTime(utcNow, -1);
            var clearFloor = sensor.HistoryClearedTo ?? DateTime.MinValue;

            return keepFloor > clearFloor ? keepFloor : clearFloor;
        }

        /// <summary>True when the large file store has a record of the sensor (any state). Opens the store's records.</summary>
        internal bool HasLargeFiles(BaseSensorModel sensor) =>
            sensor is FileSensorModel && _database.LargeFiles is { } store && store.HasFiles(sensor.Id);


        /// <summary>
        /// Writes the value of a large file on its product's queue (#1883). Without <paramref name="rollForward"/>: picks
        /// the value's time (now, or the next tick no row of the sensor holds), writes the record as Verified durably, then
        /// adds the value as any value is added (its checks, its alerts, its row). With it: writes the row of a verified
        /// record again at the record's time, unless a row is there. Never cancelled once queued: the record and the row
        /// must not be left half-done by a caller that went away.
        /// </summary>
        internal async Task<LargeFileValueRequest> AddLargeFileValueAsync(LargeFileRecord record, byte[] hash, bool rollForward)
        {
            var request = new LargeFileValueRequest(record, hash, rollForward);
            var result = await ProcessRequestAsync(record.ProductId, request, CancellationToken.None);

            if (!result.IsOk && request.Outcome == LargeFileValueOutcome.None)
            {
                request.Outcome = LargeFileValueOutcome.Fault;
                request.Error = result.Error;
            }

            return request;
        }

        /// <summary>The pass's judgement of verified and packed records of one product, on its queue (see <see cref="CheckLargeFiles"/>).</summary>
        internal async Task<LargeFileCheckRequest> CheckLargeFilesAsync(Guid productId, IReadOnlyList<LargeFileRecord> records, DateTime utcNow, CancellationToken token)
        {
            var request = new LargeFileCheckRequest(productId, records, utcNow);
            var result = await ProcessRequestAsync(productId, request, token);

            return result.IsOk ? request : null;
        }


        private void AddLargeFileValue(LargeFileValueRequest request)
        {
            var record = request.Record;
            var store = _database.LargeFiles;

            try
            {
                if (!TryGetSensorById(record.SensorId, out var sensor) || sensor is not FileSensorModel || sensor.Root.Id != record.ProductId)
                {
                    request.Outcome = LargeFileValueOutcome.SensorGone;
                    request.Error = $"Sensor {record.SensorId} is no longer a File sensor of product {record.ProductId}.";
                    return;
                }

                DateTime time;

                if (request.RollForward)
                {
                    time = record.ValueTimeUtc ?? throw new InvalidOperationException($"The verified record {record.FileId:N} has no value time.");

                    if (_database.GetSensorValue(sensor.Id, time.Ticks) is not null)
                    {
                        request.Outcome = LargeFileValueOutcome.RowPresent;
                        return;
                    }
                }
                else
                {
                    if (sensor.State == SensorState.Blocked)
                    {
                        request.Outcome = LargeFileValueOutcome.SensorGone;
                        request.Error = $"Sensor '{sensor.FullPath}' is blocked.";
                        return;
                    }

                    if (!TryFindFreeValueTime(sensor, LargeFileClock(), out time))
                    {
                        request.Outcome = LargeFileValueOutcome.Fault;
                        request.Error = $"No free value time was found for sensor '{sensor.FullPath}'.";
                        return;
                    }

                    // The record says Verified on the disk before the row exists: a crash in between is rolled forward
                    // from the record by the store's pass.
                    record = store.MarkVerified(record.FileId, request.Hash, time);
                }

                var value = new LargeFileValue
                {
                    Time = time,
                    ReceivingTime = time,
                    Status = SensorStatus.Ok,
                    Value = [],
                    Name = record.Name,
                    Extension = record.Extension,
                    OriginalSize = record.Size,
                    FileId = record.FileId,
                    Hash = record.Hash,
                    SourceModified = record.SourceModifiedUtc,
                };

                var previousLastTime = sensor.LastValue?.Time;

                if (sensor.State != SensorState.Blocked && TryAddValueWithDeliveryStamp(sensor, value, out var added))
                    PersistAddedValue(sensor, sensor.Path, sensor.Id, value, added, previousLastTime);
                else
                {
                    // The file is verified: its value must exist whatever the sensor's own checks say (a singleton gate, a
                    // blocked sensor on a roll-forward), or the record and the history disagree.
                    SaveSensorValueToDb(value, sensor.Id);
                    _logger.Warn($"Sensor '{sensor.FullPath}' did not take the large value of file {record.FileId:N} through its checks; its row was written directly");
                }

                // The write of a value row reports nothing (a failure is only logged): read it back.
                var row = _database.GetSensorValue(sensor.Id, time.Ticks);

                if (row is null || !LargeFileValue.IsLargeFileRow(row) || (sensor.Convert(row) as LargeFileValue)?.FileId != record.FileId)
                {
                    request.Outcome = LargeFileValueOutcome.Fault;
                    request.Error = $"The value row of large file {record.FileId:N} could not be written for sensor '{sensor.FullPath}' at {time:O}.";
                    return;
                }

                request.ValueTimeUtc = time;
                request.Outcome = request.RollForward ? LargeFileValueOutcome.RolledForward : LargeFileValueOutcome.Added;
            }
            catch (Exception ex)
            {
                request.Outcome = LargeFileValueOutcome.Fault;
                request.Error = ex.Message;
                request.Exception = ex;
            }
        }

        // Now, or the next tick with no row of the sensor: a value's key is the sensor and its time, so two files must never
        // share one. The cached newest value and a point read of the key are both asked (an out-of-order row is not cached).
        private bool TryFindFreeValueTime(BaseSensorModel sensor, DateTime utcNow, out DateTime time)
        {
            var ticks = utcNow.Ticks;

            for (var probe = 0; probe < MaxValueTimeProbes; probe++, ticks++)
            {
                if (sensor.LastValue?.Time.Ticks == ticks)
                    continue;

                // null: no row; an empty array: the read failed, so the key is not known to be free.
                if (_database.GetSensorValue(sensor.Id, ticks) is not null)
                    continue;

                time = new DateTime(ticks, DateTimeKind.Utc);
                return true;
            }

            time = default;
            return false;
        }

        // For each verified or packed record of the product: is its value still there? Decided on the product's queue, so
        // that a retention pass, a clear, a removal or the finish of an upload is never half-seen. Only decisions are taken
        // here; the files are removed by the caller, and a missing row inside the kept range is written again here.
        private void CheckLargeFiles(LargeFileCheckRequest request)
        {
            foreach (var record in request.Records)
            {
                try
                {
                    request.Verdicts.Add(Judge(record, request.UtcNow));
                }
                catch (Exception ex)
                {
                    request.Verdicts.Add(new LargeFileVerdict(record, LargeFileVerdictKind.ReadFailed, ex.Message));
                }
            }
        }

        private LargeFileVerdict Judge(LargeFileRecord record, DateTime utcNow)
        {
            if (!TryGetSensorById(record.SensorId, out var sensor))
                return new LargeFileVerdict(record, LargeFileVerdictKind.SensorGone, "its sensor was removed");

            if (sensor is not FileSensorModel || sensor.Root.Id != record.ProductId)
                return new LargeFileVerdict(record, LargeFileVerdictKind.Elsewhere,
                    $"its sensor '{sensor.FullPath}' is a {sensor.Type} sensor of product {sensor.Root.Id} now");

            if (record.State == LargeFileState.Uploading)
                return new LargeFileVerdict(record, LargeFileVerdictKind.Keep, null);

            if (record.ValueTimeUtc is not { } time)
                return new LargeFileVerdict(record, LargeFileVerdictKind.RowDisagrees, "the record has no value time");

            var row = _database.GetSensorValue(sensor.Id, time.Ticks);

            if (row is { Length: 0 })
                return new LargeFileVerdict(record, LargeFileVerdictKind.ReadFailed, $"the row at {time:O} could not be read");

            if (row is not null)
                return LargeFileValue.IsLargeFileRow(row) && (sensor.Convert(row) as LargeFileValue)?.FileId == record.FileId
                    ? new LargeFileVerdict(record, LargeFileVerdictKind.Keep, null)
                    : new LargeFileVerdict(record, LargeFileVerdictKind.RowDisagrees, $"the row of sensor '{sensor.FullPath}' at {time:O} is not this file");

            var floor = HistoryFloor(sensor, utcNow);

            if (time < floor)
                return new LargeFileVerdict(record, LargeFileVerdictKind.ValueGone, $"its value at {time:O} is below the history floor {floor:O} of sensor '{sensor.FullPath}' (retention or a clear)");

            // Inside the kept range and no row: the row was never written (a crash between the verified record and the row)
            // or was lost. Written again from the record (the owner's decision, #1883), never silently.
            var rolled = new LargeFileValueRequest(record, record.Hash, RollForward: true);
            AddLargeFileValue(rolled);

            return rolled.Outcome is LargeFileValueOutcome.RolledForward
                ? new LargeFileVerdict(record, LargeFileVerdictKind.RolledForward, $"its value at {time:O} was missing inside the kept history of sensor '{sensor.FullPath}'")
                : new LargeFileVerdict(record, LargeFileVerdictKind.ReadFailed, $"its missing value at {time:O} could not be written again: {rolled.Error}");
        }

        // A removed sensor (alone, with its product, by self-destroy): every file of it goes now, uploads included.
        private void RemoveLargeFilesOfSensor(BaseSensorModel sensor)
        {
            if (sensor is not FileSensorModel || _database.LargeFiles is not { } store)
                return;

            try
            {
                if (!store.HasFiles(sensor.Id))
                    return;

                foreach (var record in store.GetFiles().Where(r => r.SensorId == sensor.Id))
                    store.Remove(record.FileId, $"its sensor '{sensor.FullPath}' was removed");
            }
            catch (LargeFileStoreException ex)
            {
                // The store's pass removes them later: their sensor is gone.
                _logger.Error(ex, $"The large files of the removed sensor '{sensor.FullPath}' could not be removed now");
            }
        }

        // A clear or a retention pass erased the rows of [from, to]: the files of the large values among them go now.
        private void RemoveLargeFilesOfClearedRange(BaseSensorModel sensor, DateTime from, DateTime to)
        {
            if (sensor is not FileSensorModel || _database.LargeFiles is not { } store)
                return;

            try
            {
                if (!store.HasFiles(sensor.Id))
                    return;

                foreach (var record in store.GetFiles().Where(r => r.SensorId == sensor.Id && r.State != LargeFileState.Uploading &&
                                                                   r.ValueTimeUtc is { } time && time >= from && time <= to))
                    store.Remove(record.FileId, $"the history of sensor '{sensor.FullPath}' was cleared up to {to:O}");
            }
            catch (LargeFileStoreException ex)
            {
                // The store's pass removes them later: their rows are gone and their times are below the floor.
                _logger.Error(ex, $"The large files of the cleared history of sensor '{sensor.FullPath}' could not be removed now");
            }
        }
    }
}


namespace HSMServer.Core.Model.Requests
{
    using HSMServer.Core.SensorsUpdatesQueue;

    internal enum LargeFileValueOutcome
    {
        None,
        Added,
        RolledForward,
        RowPresent,
        SensorGone,
        Fault,
    }


    /// <summary>The value row of a large file, written on the product's queue (#1883).</summary>
    internal sealed record LargeFileValueRequest(LargeFileRecord Record, byte[] Hash, bool RollForward) : IUpdateRequest
    {
        public LargeFileValueOutcome Outcome { get; set; }

        public string Error { get; set; }

        public Exception Exception { get; set; }

        public DateTime? ValueTimeUtc { get; set; }

        public override string ToString() => $"Large file value, FileId = {Record.FileId:N}, SensorId = {Record.SensorId}, RollForward = {RollForward}";
    }


    internal enum LargeFileVerdictKind
    {
        Keep,
        SensorGone,
        ValueGone,
        RolledForward,
        RowDisagrees,
        Elsewhere,
        ReadFailed,
    }


    internal sealed record LargeFileVerdict(LargeFileRecord Record, LargeFileVerdictKind Kind, string Reason);


    /// <summary>The records of one product judged against the sensors and their rows (#1883).</summary>
    internal sealed record LargeFileCheckRequest(Guid ProductId, IReadOnlyList<LargeFileRecord> Records, DateTime UtcNow) : IUpdateRequest
    {
        public List<LargeFileVerdict> Verdicts { get; } = [];

        public override string ToString() => $"Large file check, ProductId = {ProductId}, Count = {Records.Count}";
    }
}
