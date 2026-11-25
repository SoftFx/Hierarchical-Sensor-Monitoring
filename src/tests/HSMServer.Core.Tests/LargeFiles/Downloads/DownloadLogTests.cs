using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseSettings;
using HSMServer.Core.LargeFiles.Downloads;
using HSMServer.Core.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Downloads
{
    // The store in memory, ordered as the LevelDB key orders it: (ticks, id).
    internal sealed class InMemoryDownloadLogStore : IDownloadLogStore
    {
        private readonly object _lock = new();
        private readonly List<DownloadRecordEntity> _rows = [];

        internal bool Fail { get; set; }

        internal int Puts { get; private set; }

        internal IReadOnlyList<DownloadRecordEntity> Rows
        {
            get
            {
                lock (_lock)
                    return [.. _rows];
            }
        }


        public void Put(DownloadRecordEntity entity)
        {
            lock (_lock)
            {
                if (Fail)
                    throw new IOException("Simulated download log write failure");

                Puts++;
                _rows.Add(entity);
            }
        }

        public List<DownloadRecordEntity> Read(long fromTicks, long toTicks, Guid? productId, int limit)
        {
            lock (_lock)
                return _rows.Where(r => r.TimeTicks >= fromTicks && r.TimeTicks <= toTicks && (productId is null || r.ProductId == productId))
                            .OrderByDescending(r => r.TimeTicks).Take(limit).ToList();
        }

        public int RemoveOlderThan(long ticksExclusive)
        {
            lock (_lock)
                return _rows.RemoveAll(r => r.TimeTicks < ticksExclusive);
        }
    }


    public sealed class DownloadLogTests
    {
        private static readonly DateTime Start = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

        private readonly InMemoryDownloadLogStore _store = new();
        private readonly DownloadLog _log;

        private readonly DownloadCaller _user = new(DownloadChannel.Web, Guid.NewGuid(), "andrei", null, null, "10.0.0.1");
        private readonly DownloadTarget _archive = Target(Guid.NewGuid());


        public DownloadLogTests()
        {
            _log = new DownloadLog(_store);
        }


        [Fact]
        public void Requests_Within_A_Renewed_Window_Make_One_Record()
        {
            var written = 0;

            // Every 9 minutes for an hour: each request falls within the window the previous one renewed.
            for (var minute = 0; minute <= 60; minute += 9)
                if (_log.Record(_user, _archive, Start.AddMinutes(minute)))
                    written++;

            Assert.Equal(1, written);
            Assert.Single(_store.Rows);
            Assert.Equal(Start, new DateTime(_store.Rows[0].TimeTicks, DateTimeKind.Utc));
        }

        [Fact]
        public void A_Gap_Longer_Than_The_Window_Makes_A_New_Record()
        {
            Assert.True(_log.Record(_user, _archive, Start));
            Assert.False(_log.Record(_user, _archive, Start.AddMinutes(9)));
            Assert.True(_log.Record(_user, _archive, Start.AddMinutes(9 + 11)));
            Assert.False(_log.Record(_user, _archive, Start.AddMinutes(9 + 11 + 1)));

            // Exactly the window after the last request: the window has ended.
            Assert.True(_log.Record(_user, _archive, Start.AddMinutes(9 + 11 + 1 + 10)));

            Assert.Equal(3, _store.Rows.Count);
        }

        [Fact]
        public void Different_Callers_And_Archives_Make_Separate_Records()
        {
            var otherUser = _user with { UserId = Guid.NewGuid(), UserName = "maria" };
            var otherArchive = Target(_archive.ProductId);

            Assert.True(_log.Record(_user, _archive, Start));
            Assert.True(_log.Record(otherUser, _archive, Start));
            Assert.True(_log.Record(_user, otherArchive, Start));
            Assert.False(_log.Record(_user, _archive, Start.AddMinutes(1)));
            Assert.False(_log.Record(otherUser, _archive, Start.AddMinutes(1)));
            Assert.False(_log.Record(_user, otherArchive, Start.AddMinutes(1)));

            Assert.Equal(3, _store.Rows.Count);
        }

        [Fact]
        public void A_Token_Is_A_Caller_Of_Its_Own_Apart_From_Its_Owner()
        {
            var token = new DownloadCaller(DownloadChannel.Api, _user.UserId, _user.UserName, Guid.NewGuid(), "ci token", "10.0.0.2");
            var otherToken = token with { TokenId = Guid.NewGuid(), TokenName = "other token" };

            Assert.NotEqual(_user.Key, token.Key);

            Assert.True(_log.Record(_user, _archive, Start));
            Assert.True(_log.Record(token, _archive, Start));
            Assert.True(_log.Record(otherToken, _archive, Start));
            Assert.False(_log.Record(token with { ClientIp = "10.0.0.3" }, _archive, Start.AddMinutes(2)));

            var row = _store.Rows.Single(r => r.TokenId == token.TokenId);

            Assert.Equal((byte)DownloadChannel.Api, row.Channel);
            Assert.Equal(_user.UserId, row.UserId);
            Assert.Equal("ci token", row.TokenName);
        }

        [Fact]
        public void A_Record_Carries_The_Caller_And_The_Target()
        {
            _log.Record(_user, _archive, Start);

            var record = _log.Read(_archive.ProductId, Start, Start, 10).Single();

            Assert.NotEqual(Guid.Empty, record.Id);
            Assert.Equal(Start, record.TimeUtc);
            Assert.Equal(DateTimeKind.Utc, record.TimeUtc.Kind);
            Assert.Equal(_archive.ProductId, record.ProductId);
            Assert.Equal(_archive.SensorId, record.SensorId);
            Assert.Equal(_archive.FileId, record.FileId);
            Assert.Equal(_archive.ArchiveId, record.ArchiveId);
            Assert.Equal(_archive.PasswordVersion, record.PasswordVersion);
            Assert.Equal(_archive.FileName, record.FileName);
            Assert.Equal(DownloadChannel.Web, record.Channel);
            Assert.Equal(_user.UserId, record.UserId);
            Assert.Equal("andrei", record.UserName);
            Assert.Null(record.TokenId);
            Assert.Null(record.TokenName);
            Assert.Equal("10.0.0.1", record.ClientIp);
        }

        [Fact]
        public void A_Failed_Write_Throws_Counts_And_The_Next_Request_Writes_Again()
        {
            _store.Fail = true;

            var ex = Assert.Throws<DownloadLogException>(() => _log.Record(_user, _archive, Start));

            Assert.IsType<IOException>(ex.InnerException);
            Assert.Equal(1, _log.FailedWrites);
            Assert.Equal(0, _log.WindowCount);

            Assert.Throws<DownloadLogException>(() => _log.Record(_user, _archive, Start.AddMinutes(1)));
            Assert.Equal(2, _log.FailedWrites);

            _store.Fail = false;

            Assert.True(_log.Record(_user, _archive, Start.AddMinutes(2)));
            Assert.False(_log.Record(_user, _archive, Start.AddMinutes(3)));
            Assert.Single(_store.Rows);
            Assert.Equal(2, _log.FailedWrites);
        }

        [Fact]
        public void A_Failed_Write_After_An_Ended_Window_Does_Not_Renew_It()
        {
            Assert.True(_log.Record(_user, _archive, Start));

            _store.Fail = true;
            Assert.Throws<DownloadLogException>(() => _log.Record(_user, _archive, Start.AddMinutes(30)));
            _store.Fail = false;

            Assert.True(_log.Record(_user, _archive, Start.AddMinutes(31)));
            Assert.Equal(2, _store.Rows.Count);
        }

        [Fact]
        public void The_Windows_Are_Bounded_Ended_Ones_First_Then_The_Oldest()
        {
            var log = new DownloadLog(_store, new DownloadLogOptions { MaxWindows = 3 });
            var callers = Enumerable.Range(0, 5).Select(i => _user with { UserId = Guid.NewGuid(), UserName = $"user{i}" }).ToArray();

            log.Record(callers[0], _archive, Start);
            log.Record(callers[1], _archive, Start.AddMinutes(5));
            log.Record(callers[2], _archive, Start.AddMinutes(11));

            // Caller 0's window ended at minute 10: it goes, the others stay.
            log.Record(callers[3], _archive, Start.AddMinutes(12));

            Assert.Equal(3, log.WindowCount);
            Assert.False(log.Record(callers[1], _archive, Start.AddMinutes(13)));
            Assert.False(log.Record(callers[2], _archive, Start.AddMinutes(13)));

            // Every window is live: the oldest (caller 3, last at minute 12) goes.
            log.Record(callers[4], _archive, Start.AddMinutes(14));

            Assert.Equal(3, log.WindowCount);
            Assert.True(log.Record(callers[3], _archive, Start.AddMinutes(15)));
            Assert.True(log.WindowCount <= 3);
        }

        [Fact]
        public void The_Default_Bound_Holds_Under_Many_Callers()
        {
            for (var i = 0; i < 10_050; i++)
                _log.Record(_user with { UserId = Guid.NewGuid() }, _archive, Start.AddMilliseconds(i));

            Assert.Equal(10_000, _log.WindowCount);
        }

        [Fact]
        public void Parallel_Requests_Of_One_Caller_For_One_Archive_Make_One_Record()
        {
            var results = new bool[64];

            Parallel.For(0, results.Length, i => results[i] = _log.Record(_user, _archive, Start.AddSeconds(i % 3)));

            Assert.Equal(1, results.Count(r => r));
            Assert.Single(_store.Rows);
        }

        [Fact]
        public void Read_Filters_By_Product_And_Range_Newest_First_With_A_Limit()
        {
            var product = _archive.ProductId;
            var otherProduct = Target(Guid.NewGuid());

            for (var i = 0; i < 5; i++)
            {
                _log.Record(_user with { UserId = Guid.NewGuid() }, _archive, Start.AddHours(i));
                _log.Record(_user with { UserId = Guid.NewGuid() }, otherProduct, Start.AddHours(i));
            }

            var all = _log.Read(product, Start, Start.AddHours(10), 100);

            Assert.Equal(5, all.Count);
            Assert.All(all, r => Assert.Equal(product, r.ProductId));
            Assert.Equal(all.OrderByDescending(r => r.TimeUtc), all);

            var range = _log.Read(product, Start.AddHours(1), Start.AddHours(3), 100);

            Assert.Equal([Start.AddHours(3), Start.AddHours(2), Start.AddHours(1)], range.Select(r => r.TimeUtc));

            Assert.Equal([Start.AddHours(4), Start.AddHours(3)], _log.Read(product, Start, Start.AddHours(10), 2).Select(r => r.TimeUtc));
            Assert.Empty(_log.Read(product, Start, Start.AddHours(10), 0));
        }

        [Fact]
        public void RemoveOlderThan_Removes_Strictly_Older_Records()
        {
            for (var i = 0; i < 4; i++)
                _log.Record(_user with { UserId = Guid.NewGuid() }, _archive, Start.AddDays(i));

            Assert.Equal(2, _log.RemoveOlderThan(Start.AddDays(2)));
            Assert.Equal([Start.AddDays(3), Start.AddDays(2)], _log.Read(_archive.ProductId, DateTime.MinValue, DateTime.MaxValue, 10).Select(r => r.TimeUtc));
        }

        [Fact]
        public void Options_Must_Be_Sane()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DownloadLog(_store, new DownloadLogOptions { Window = TimeSpan.Zero }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DownloadLog(_store, new DownloadLogOptions { MaxWindows = 0 }));
            Assert.Throws<ArgumentNullException>(() => new DownloadLog(null));
        }


        internal static DownloadTarget Target(Guid productId) =>
            new(productId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "dump.dmp");
    }


    // The LevelDB store behind the log: a real database in a temp folder.
    public sealed class DownloadLogDatabaseTests : IDisposable
    {
        private static readonly DateTime Start = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

        private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hsm-download-log-{Guid.NewGuid():N}");
        private readonly DatabaseCoreManager _manager;


        public DownloadLogDatabaseTests()
        {
            _manager = new DatabaseCoreManager(_folder);
        }

        public void Dispose()
        {
            _manager.DatabaseCore?.Dispose();

            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }


        [Fact]
        public void The_Database_Opens_On_First_Use()
        {
            Assert.False(Directory.Exists(_manager.DatabaseCore.DatabaseSettings.PathToDownloadLogDb));

            _manager.DatabaseCore.DownloadLog.Read(0, long.MaxValue, null, 10);

            Assert.True(Directory.Exists(_manager.DatabaseCore.DatabaseSettings.PathToDownloadLogDb));
        }

        [Fact]
        public void Records_Round_Trip_Through_LevelDB_And_Survive_A_Reopen()
        {
            var log = new DownloadLog(_manager.DatabaseCore.DownloadLog);
            var caller = new DownloadCaller(DownloadChannel.Api, Guid.NewGuid(), "andrei", Guid.NewGuid(), "ci token", "10.0.0.2");
            var target = DownloadLogTests.Target(Guid.NewGuid());
            var other = DownloadLogTests.Target(Guid.NewGuid());

            for (var i = 0; i < 5; i++)
            {
                Assert.True(log.Record(caller with { TokenId = Guid.NewGuid() }, target, Start.AddDays(i)));
                Assert.True(log.Record(caller with { TokenId = Guid.NewGuid() }, other, Start.AddDays(i)));
            }

            // Two records at the same tick are both kept (the record id is a part of the key).
            Assert.True(log.Record(caller with { TokenId = Guid.NewGuid() }, target, Start.AddDays(4)));

            var settings = _manager.DatabaseCore.DatabaseSettings;

            _manager.DatabaseCore.Dispose();

            // DatabaseCore.Dispose leaves the layout, change-set and snapshot databases open: the reopened core gets fresh
            // ones of those, and the same download log.
            using var reopened = new HSMDatabase.DatabaseWorkCore.DatabaseCore(((HSMDatabase.Settings.DatabaseSettings)settings) with
            {
                ServerLayoutDatabaseName = settings.ServerLayoutDatabaseName + "_reopen",
                ChangeSetsDatabaseName = settings.ChangeSetsDatabaseName + "_reopen",
                SnaphotsDatabaseName = settings.SnaphotsDatabaseName + "_reopen",
            });
            var reread = new DownloadLog(reopened.DownloadLog);

            var all = reread.Read(target.ProductId, DateTime.MinValue, DateTime.MaxValue, 100);

            Assert.Equal(6, all.Count);
            Assert.Equal([Start.AddDays(4), Start.AddDays(4), Start.AddDays(3), Start.AddDays(2), Start.AddDays(1), Start], all.Select(r => r.TimeUtc));
            Assert.All(all, r =>
            {
                Assert.Equal(target.ArchiveId, r.ArchiveId);
                Assert.Equal(DownloadChannel.Api, r.Channel);
                Assert.Equal("ci token", r.TokenName);
                Assert.Equal("dump.dmp", r.FileName);
                Assert.Equal(3, r.PasswordVersion);
            });

            // The range is inclusive at both ends.
            Assert.Equal([Start.AddDays(3), Start.AddDays(2), Start.AddDays(1)],
                reread.Read(target.ProductId, Start.AddDays(1), Start.AddDays(3), 100).Select(r => r.TimeUtc));

            Assert.Equal(2, reread.Read(target.ProductId, Start, Start.AddDays(10), 2).Count);
            Assert.Equal(11, reopened.DownloadLog.Read(0, long.MaxValue, null, 100).Count);

            Assert.Equal(4, reread.RemoveOlderThan(Start.AddDays(2)));
            Assert.Equal([Start.AddDays(4), Start.AddDays(4), Start.AddDays(3), Start.AddDays(2)],
                reread.Read(target.ProductId, DateTime.MinValue, DateTime.MaxValue, 100).Select(r => r.TimeUtc));
            Assert.Equal(0, reread.RemoveOlderThan(Start.AddDays(2)));
        }
    }
}
