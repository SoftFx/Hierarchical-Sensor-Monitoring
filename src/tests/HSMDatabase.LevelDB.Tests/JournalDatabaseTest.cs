using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Xunit;

namespace HSMDatabase.LevelDB.Tests
{
    public class JournalDatabaseTest : DatabaseCoreTestsBase<JournalDatabaseFixture>, IClassFixture<DatabaseRegisterFixture>
    {
        private readonly IDatabaseCore _databaseCore;

        public JournalDatabaseTest(JournalDatabaseFixture fixture, DatabaseRegisterFixture registerFixture) : base(fixture, registerFixture)
        {
            _databaseCore = _databaseCoreManager.DatabaseCore;
        }


        [Fact]
        public async Task GetValues_Count_Test()
        {
            const int historyValuesCount = 11;
            var sensorId = Guid.NewGuid();
            var journals = GenerateJournalEntities(sensorId, historyValuesCount);

            foreach (var journal in journals)
            {
                _databaseCore.AddJournalValue(journal.Item1, journal.Item2);
            }

            var actualJournals = (await _databaseCore.GetJournalValuesPage(sensorId, DateTime.MinValue, DateTime.MaxValue, historyValuesCount)
                .Flatten()).Select(x => JsonSerializer.Deserialize<JournalEntity>(x)).ToList();

            Assert.Equal(journals.Count, actualJournals.Count);

            for (int i = 0; i < historyValuesCount; i++)
            {
                var actual = actualJournals[i];
                var expected = journals[i];
                Assert.Equal(expected.Item2.Value, actual.Value);
            }
        }

        private List<(Key, JournalEntity)> GenerateJournalEntities(Guid sensorId, int count)
        {
            List<(Key, JournalEntity)> result = new(count);

            for (int i = 0; i < count; i++)
            {
                var key = new Key(sensorId, DateTime.UtcNow.Ticks);
                result.Add((key, new JournalEntity()
                {
                    Value = $"TEST_{i}"
                }));
            }

            return result;
        }
    }
}