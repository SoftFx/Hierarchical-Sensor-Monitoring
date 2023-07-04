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
            var journalType = RecordType.Actions;

            foreach (var journal in journals)
            {
                _databaseCore.AddJournalValue(journal.Item1, journal.Item2);
            }

            await Task.Delay(2000);
            var actualJournals = (await _databaseCore.GetJournalValuesPage(sensorId, DateTime.MinValue, DateTime.MaxValue, journalType, historyValuesCount)
                .Flatten()).Select(x => JsonSerializer.Deserialize<JournalEntity>(x)).ToList();

            Assert.Equal(journals.Count, actualJournals.Count);
        }

        [Fact]
        public async Task JournalBorderTests()
        {
            var firstValue = "Test_Zero";
            var secondValue = "Test_Max";
            
            await JournalDatabaseTestOrder(Guid.Empty,RecordType.Actions, RecordType.Actions, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.Empty,RecordType.Actions, RecordType.Changes, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.Empty,RecordType.Changes, RecordType.Changes, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.Empty,RecordType.Changes, RecordType.Actions, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.NewGuid(),RecordType.Actions, RecordType.Actions, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.NewGuid(),RecordType.Actions, RecordType.Changes, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.NewGuid(),RecordType.Changes, RecordType.Changes, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.NewGuid(),RecordType.Changes, RecordType.Actions, firstValue, secondValue);
        }
        
        private async Task JournalDatabaseTestOrder(Guid guid, RecordType first = RecordType.Actions, RecordType second = RecordType.Actions, string firstValue = "", string secondValue = "")
        {
            GenerateBorderValues(guid, first, second, firstValue, secondValue);
            await Task.Delay(100);
            
            if (first == second)
            {
                var databaseNoData = await GetJournalValues(guid, DateTime.MinValue, DateTime.MaxValue, GetAlterType(first));
                Assert.Empty(databaseNoData);
                
                var databaseData = await GetJournalValues(guid, DateTime.MinValue, DateTime.MaxValue, first);
                Assert.Equal(2, databaseData.Count);
                
                Assert.Equal(firstValue, databaseData[0]?.Value);
                Assert.Equal(secondValue, databaseData[1]?.Value);
            }
            else
            {
                var databaseSingleActionsData = await GetJournalValues(guid, DateTime.MinValue, DateTime.MaxValue, RecordType.Actions);
                var databaseSingleChangesData = await GetJournalValues(guid, DateTime.MinValue, DateTime.MaxValue, RecordType.Changes);
                Assert.Single(databaseSingleActionsData);
                Assert.Single(databaseSingleChangesData);
            }
            
            _databaseCore.RemoveJournalValue(guid);
            await Task.Delay(100);
        }

        private RecordType GetAlterType(RecordType currType) => currType switch
        {
            RecordType.Actions => RecordType.Changes,
            _ => RecordType.Actions
        };

        private void GenerateBorderValues(Guid guid, RecordType first = RecordType.Actions, RecordType second = RecordType.Actions, string firstValue = "", string secondValue = "")
        {
            var zeroValue = new JournalEntity(firstValue);
            var maxValue = new JournalEntity(secondValue);
            var keyZero = new JournalKey(guid, DateTime.MinValue.Ticks, first);
            var keyMax = new JournalKey(guid, DateTime.UtcNow.Ticks, second);
            
            _databaseCore.AddJournalValue(keyMax, maxValue);
            _databaseCore.AddJournalValue(keyZero, zeroValue);
        }

        private async Task<List<JournalEntity?>> GetJournalValues(Guid guid, DateTime from, DateTime to, RecordType type = RecordType.Actions, int count = 5000) => 
            (await _databaseCore.GetJournalValuesPage(guid, from, to, type, count).Flatten()).Select(x => JsonSerializer.Deserialize<JournalEntity>(x)).ToList();

        private List<(JournalKey, JournalEntity)> GenerateJournalEntities(Guid sensorId, int count)
        {
            List<(JournalKey, JournalEntity)> result = new(count);

            for (int i = 0; i < count; i++)
            {
                var key = new JournalKey(sensorId, DateTime.UtcNow.Ticks, RecordType.Actions);
                result.Add((key, new JournalEntity($"TEST_{i}")));
            }

            return result;
        }
    }
}