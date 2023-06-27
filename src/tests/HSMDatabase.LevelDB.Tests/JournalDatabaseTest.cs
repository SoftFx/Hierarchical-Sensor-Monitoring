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
            const int historyValuesCount = 1000;
            var sensorId = Guid.NewGuid();
            var journals = GenerateJournalEntities(sensorId, historyValuesCount);
            var journalType = JournalType.Actions;

            foreach (var journal in journals)
            {
                _databaseCore.AddJournalValue(journal.Item1, journal.Item2);
            }

            await Task.Delay(2000);
            var actualJournals = (await _databaseCore.GetJournalValuesPage(sensorId, DateTime.MinValue, DateTime.MaxValue, journalType, historyValuesCount)
                .Flatten()).Select(x => JsonSerializer.Deserialize<JournalEntity>(x)).ToList();

            Assert.Equal(journals.Count, actualJournals.Count);
        }

        private List<(Key, JournalEntity)> GenerateJournalEntities(Guid sensorId, int count)
        {
            List<(Key, JournalEntity)> result = new(count);

            for (int i = 0; i < count; i++)
            {
                var key = new Key(sensorId, DateTime.UtcNow.Ticks, JournalType.Actions);
                result.Add((key, new JournalEntity()
                {
                    Value = $"TEST_{i}"
                }));
            }

            return result;
        }
    }
}