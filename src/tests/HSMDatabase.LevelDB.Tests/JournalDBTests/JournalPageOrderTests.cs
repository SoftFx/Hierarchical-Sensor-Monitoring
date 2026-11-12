using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.JournalDBTests
{
    // The journal is stored in weekly shards and, inside a shard, under
    // sensorId|type|ticks keys, so the Actions and Changes of one shard live in
    // two separate key ranges. A count-limited page must still pick the records
    // nearest to its starting edge by time: the newest ones for a negative
    // count, the oldest ones for a positive count.
    public class JournalPageOrderTests : DatabaseCoreTestsBase<JournalPageOrderFixture>, IClassFixture<DatabaseRegisterFixture>
    {
        // Three consecutive Monday-based weeks, one journal shard each.
        private static readonly DateTime Week1 = new(2024, 1, 1);
        private static readonly DateTime Week2 = new(2024, 1, 8);
        private static readonly DateTime Week3 = new(2024, 1, 15);

        private static readonly DateTime From = Week1.AddDays(-1);
        private static readonly DateTime To = Week3.AddDays(8);

        private readonly IDatabaseCore _databaseCore;


        public JournalPageOrderTests(JournalPageOrderFixture fixture, DatabaseRegisterFixture registerFixture) : base(fixture, registerFixture)
        {
            _databaseCore = _databaseCoreManager.DatabaseCore;
        }


        [Fact]
        public async Task NegativeCount_AcrossWeeks_ReturnsNewestRecords()
        {
            var id = SeedThreeWeeks();

            var actual = await Read(id, From, To, -3);

            Assert.Equal(["W3-C13", "W3-A12", "W3-C11"], actual);
        }

        [Fact]
        public async Task NegativeCount_OneWeekWithBothTypes_MergesTypesByTime()
        {
            var id = SeedThreeWeeks();

            var actual = await Read(id, Week3, Week3.AddDays(7), -2);

            Assert.Equal(["W3-C13", "W3-A12"], actual);
        }

        [Fact]
        public async Task PositiveCount_OneWeekWithBothTypes_MergesTypesByTime()
        {
            var id = SeedThreeWeeks();

            var actual = await Read(id, Week3, Week3.AddDays(7), 2);

            Assert.Equal(["W3-A10", "W3-C11"], actual);
        }

        [Fact]
        public async Task PositiveCount_AcrossWeeks_ReturnsOldestRecords()
        {
            var id = SeedThreeWeeks();

            var actual = await Read(id, From, To, 4);

            Assert.Equal(["W1-C10", "W1-C11", "W1-C12", "W2-C10"], actual);
        }

        [Fact]
        public async Task FullRead_ReturnsEveryRecordInTimeOrder()
        {
            var id = SeedThreeWeeks();
            string[] ascending =
            [
                "W1-C10", "W1-C11", "W1-C12",
                "W2-C10", "W2-C11", "W2-C12",
                "W3-A10", "W3-C11", "W3-A12", "W3-C13",
            ];

            Assert.Equal(ascending, await Read(id, From, To, 100));
            Assert.Equal(Enumerable.Reverse(ascending), await Read(id, From, To, -100));
        }


        [Fact]
        public async Task EqualTicks_AcrossTypes_DirectionsMirror()
        {
            var id = Guid.NewGuid();

            Add(id, Week2, 15, RecordType.Actions, "W2");
            Add(id, Week2, 15, RecordType.Changes, "W2");

            Assert.Equal(["W2-A15", "W2-C15"], await Read(id, From, To, 100));
            Assert.Equal(["W2-C15", "W2-A15"], await Read(id, From, To, -100));
            Assert.Equal(["W2-A15"], await Read(id, From, To, 1));
            Assert.Equal(["W2-C15"], await Read(id, From, To, -1));
        }


        private Guid SeedThreeWeeks()
        {
            var id = Guid.NewGuid();

            foreach (var hour in new[] { 10, 11, 12 })
            {
                Add(id, Week1, hour, RecordType.Changes, "W1");
                Add(id, Week2, hour, RecordType.Changes, "W2");
            }

            Add(id, Week3, 10, RecordType.Actions, "W3");
            Add(id, Week3, 11, RecordType.Changes, "W3");
            Add(id, Week3, 12, RecordType.Actions, "W3");
            Add(id, Week3, 13, RecordType.Changes, "W3");

            return id;
        }

        private void Add(Guid id, DateTime week, int hour, RecordType type, string weekLabel)
        {
            var key = new JournalKey(id, week.AddHours(hour).Ticks, type);
            var label = $"{weekLabel}-{(type == RecordType.Actions ? 'A' : 'C')}{hour}";

            _databaseCore.AddJournalValue(key, new JournalRecordEntity { OldValue = label, Initiator = "System" });
        }

        private async Task<List<string>> Read(Guid id, DateTime from, DateTime to, int count) =>
            (await _databaseCore.GetJournalValuesPage(id, from, to, JournalHistoryRequestModel.AllTypes, count).Flatten())
                .Select(x => x.Entity.OldValue).ToList();
    }
}
