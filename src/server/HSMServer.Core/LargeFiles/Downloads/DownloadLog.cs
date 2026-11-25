using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HSMServer.Core.LargeFiles.Downloads
{
    public sealed record DownloadLogOptions
    {
        /// <summary>Requests of one caller for one archive within this time of the previous one make no new record.</summary>
        public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(10);

        /// <summary>The most (caller, archive) windows kept in memory.</summary>
        public int MaxWindows { get; init; } = 10_000;
    }


    /// <summary>
    /// The download log (ADR-0022 point 15) over its own database. The requests of one caller for one archive coalesce into
    /// one record while each comes within the window of the one before it (a sliding window): a download in byte ranges or
    /// resumed after a break is one download. The windows live in memory only, bounded; after a restart the next request
    /// makes a record again. Retention is the caller's: it calls <see cref="RemoveOlderThan"/> on its own schedule.
    /// </summary>
    public sealed class DownloadLog : IDownloadLog
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly IDownloadLogStore _store;
        private readonly TimeSpan _window;
        private readonly int _maxWindows;

        private readonly object _lock = new();

        // (caller key, archive id) -> the time of the last request.
        private readonly Dictionary<(string Caller, Guid Archive), DateTime> _windows = [];

        private long _failedWrites;


        /// <summary>
        /// The newest records a read of one file looks at: a page view never walks the whole log while recording waits for it.
        /// At the expected volumes (one record per caller and archive) this covers the whole retention.
        /// </summary>
        public const int MaxScannedPerFileRead = 20_000;


        public long FailedWrites => Interlocked.Read(ref _failedWrites);

        internal int WindowCount
        {
            get
            {
                lock (_lock)
                    return _windows.Count;
            }
        }


        public DownloadLog(IDownloadLogStore store, DownloadLogOptions options = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));

            options ??= new DownloadLogOptions();

            if (options.Window <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "The window of the download log must be positive.");

            if (options.MaxWindows <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "The download log must keep at least one window.");

            _window = options.Window;
            _maxWindows = options.MaxWindows;
        }


        public bool Record(DownloadCaller caller, DownloadTarget target, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(target);

            var key = (caller.Key, target.ArchiveId);

            // The write runs under the lock: two requests of one caller for one archive at once must make one record, not two.
            lock (_lock)
            {
                if (_windows.TryGetValue(key, out var last) && nowUtc - last < _window)
                {
                    _windows[key] = nowUtc;
                    return false;
                }

                var entity = ToEntity(caller, target, nowUtc);

                try
                {
                    _store.Put(entity);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failedWrites);

                    _logger.Error(ex, $"A download of archive {target.ArchiveId} of product {target.ProductId} could not be recorded; the download is refused");

                    // No window: the next request writes again.
                    throw new DownloadLogException($"The download of archive {target.ArchiveId} could not be recorded.", ex);
                }

                if (!_windows.ContainsKey(key))
                    MakeRoom(nowUtc);

                _windows[key] = nowUtc;
            }

            return true;
        }

        public IReadOnlyList<DownloadRecord> Read(Guid productId, DateTime fromUtc, DateTime toUtc, int limit)
        {
            if (limit <= 0)
                return [];

            return _store.Read(fromUtc.Ticks, toUtc.Ticks, productId, limit).Select(ToRecord).ToList();
        }

        public IReadOnlyList<DownloadRecord> ReadFile(Guid productId, Guid fileId, int limit)
        {
            if (limit <= 0)
                return [];

            return _store.Read(DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks, productId, limit, fileId, MaxScannedPerFileRead).Select(ToRecord).ToList();
        }

        public int RemoveOlderThan(DateTime utc) => _store.RemoveOlderThan(utc.Ticks);


        // Under _lock, before a new window is added: when the map is full, the windows that ended go first, then the oldest.
        private void MakeRoom(DateTime nowUtc)
        {
            if (_windows.Count < _maxWindows)
                return;

            foreach (var (key, last) in _windows.ToArray())
                if (nowUtc - last >= _window)
                    _windows.Remove(key);

            while (_windows.Count >= _maxWindows)
            {
                var oldest = _windows.MinBy(pair => pair.Value).Key;

                _windows.Remove(oldest);
            }
        }

        private static DownloadRecordEntity ToEntity(DownloadCaller caller, DownloadTarget target, DateTime nowUtc) => new()
        {
            Id = Guid.NewGuid(),
            TimeTicks = nowUtc.Ticks,
            ProductId = target.ProductId,
            SensorId = target.SensorId,
            FileId = target.FileId,
            ArchiveId = target.ArchiveId,
            PasswordVersion = target.PasswordVersion,
            FileName = target.FileName,
            Channel = (byte)caller.Channel,
            UserId = caller.UserId,
            UserName = caller.UserName,
            TokenId = caller.TokenId,
            TokenName = caller.TokenName,
            ClientIp = caller.ClientIp,
        };

        private static DownloadRecord ToRecord(DownloadRecordEntity entity) => new()
        {
            Id = entity.Id,
            TimeUtc = new DateTime(entity.TimeTicks, DateTimeKind.Utc),
            ProductId = entity.ProductId,
            SensorId = entity.SensorId,
            FileId = entity.FileId,
            ArchiveId = entity.ArchiveId,
            PasswordVersion = entity.PasswordVersion,
            FileName = entity.FileName,
            Channel = (DownloadChannel)entity.Channel,
            UserId = entity.UserId,
            UserName = entity.UserName,
            TokenId = entity.TokenId,
            TokenName = entity.TokenName,
            ClientIp = entity.ClientIp,
        };
    }
}
