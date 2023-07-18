using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
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

        private readonly DateTime _minTime = DateTime.MinValue;
        private readonly DateTime _maxTime = DateTime.MaxValue.AddYears(-1);


        public JournalDatabaseTest(JournalDatabaseFixture fixture, DatabaseRegisterFixture registerFixture) : base(fixture, registerFixture)
        {
            _databaseCore = _databaseCoreManager.DatabaseCore;
        }


        [Fact]
        public async Task WriteAndReadTest()
        {
            const int historyValuesCount = 1000;
            var sensorId = Guid.NewGuid();
            var journals = GenerateJournalEntities(sensorId, historyValuesCount);
            var journalType = RecordType.Changes;

            foreach (var journal in journals)
                _databaseCore.AddJournalValue(journal.Item1, journal.Item2);

            await Task.Delay(1000);
            var actualJournals = (await _databaseCore.GetJournalValuesPage(sensorId, DateTime.MinValue, DateTime.MaxValue, journalType, journalType, historyValuesCount)
                .Flatten()).Select(x => (JournalKey.FromBytes(x.Key), x.Entity)).ToList();

            Assert.Equal(journals.Count, actualJournals.Count);
            for (int i = 0; i < journals.Count; i++)
            {
                CompareJournalEntity(journals[i].Item2, actualJournals[i].Entity);
                CompareJournalKey(journals[i].Item1, actualJournals[i].Item1);
            }
        }

        private void CompareJournalEntity(JournalEntity actual, JournalEntity expected)
        {
            Assert.Equal(actual.Path, expected.Path);
            Assert.Equal(actual.Value, expected.Value);
            Assert.Equal(actual.Initiator, expected.Initiator);
        }

        private void CompareJournalKey(JournalKey actual, JournalKey expected)
        {
            Assert.Equal(actual.Id, expected.Id);
            Assert.Equal(actual.Type, expected.Type);
            Assert.Equal(actual.Time, expected.Time);
        }

        [Fact]
        public async Task BorderTest()
        {
           await RandomGuidTest(100);
        }

        private async Task RandomGuidTest(int count)
        {
            var id = Guid.NewGuid();
            var expectedMinKey = JournalFactory.BuildKey(id, _minTime.Ticks);
            var expectedMaxKey = JournalFactory.BuildKey(id, _maxTime.Ticks);

            var expected = new List<(JournalKey, JournalEntity)>();
            var journals = BuildJournalEntities(count);

            for (int i = 0; i < count; i++)
            {
                if (i % 2 == 0)
                {
                    _databaseCore.AddJournalValue(expectedMinKey, journals[i]);
                    expected.Add((expectedMinKey, journals[i]));
                    expectedMinKey = JournalFactory.BuildKey(id, expectedMinKey.Time + 1);
                }
                else
                {
                    _databaseCore.AddJournalValue(expectedMaxKey, journals[i]);
                    expected.Add((expectedMaxKey, journals[i]));
                    expectedMaxKey = JournalFactory.BuildKey(id, expectedMaxKey.Time + 1);
                }
                
            }

            expected = expected.OrderBy(x => x.Item1.Time).ToList();

            await Task.Delay(500);
            
            var actualJournals = await _databaseCore.GetJournalValuesPage(id, _minTime, DateTime.MaxValue, RecordType.Actions, RecordType.Actions, TreeValuesCache.MaxHistoryCount).Flatten();
            Assert.Equal(count, actualJournals.Count);

            for (int i = 0; i < count; i++)
            {
                CompareJournalEntity(actualJournals[i].Entity, expected[i].Item2);
                CompareJournalKey(JournalKey.FromBytes(actualJournals[i].Key), expected[i].Item1);
            }
        }

        private List<JournalEntity> BuildJournalEntities(int count)
        {
            var journals = new List<JournalEntity>(count);

            for (int i = 0; i < count; i++)
                journals.Add(JournalFactory.BuildJournalEntity());

            return journals;
        }

        [Fact]
        public async Task JournalBorderTests()
        {
            var firstValue = "Test_Zero";
            var secondValue = "Test_Max";
            
            await JournalDatabaseTestOrder(Guid.Empty,RecordType.Changes, RecordType.Changes, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.Empty,RecordType.Changes, RecordType.Actions, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.Empty,RecordType.Actions, RecordType.Actions, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.Empty,RecordType.Actions, RecordType.Changes, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.NewGuid(),RecordType.Changes, RecordType.Changes, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.NewGuid(),RecordType.Changes, RecordType.Actions, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.NewGuid(),RecordType.Actions, RecordType.Actions, firstValue, secondValue);
            await JournalDatabaseTestOrder(Guid.NewGuid(),RecordType.Actions, RecordType.Changes, firstValue, secondValue);
        }
        
        private async Task JournalDatabaseTestOrder(Guid guid, RecordType first = RecordType.Changes, RecordType second = RecordType.Changes, string firstValue = "", string secondValue = "")
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
                var databaseSingleActionsData = await GetJournalValues(guid, DateTime.MinValue, DateTime.MaxValue, RecordType.Changes);
                var databaseSingleChangesData = await GetJournalValues(guid, DateTime.MinValue, DateTime.MaxValue, RecordType.Actions);
                Assert.Single(databaseSingleActionsData);
                Assert.Single(databaseSingleChangesData);
            }
            
            _databaseCore.RemoveJournalValues(guid, default);
            await Task.Delay(100);
        }

        private RecordType GetAlterType(RecordType currType) => currType switch
        {
            RecordType.Changes => RecordType.Actions,
            _ => RecordType.Changes
        };

        private void GenerateBorderValues(Guid guid, RecordType first = RecordType.Changes, RecordType second = RecordType.Changes, string firstValue = "", string secondValue = "")
        {
            var zeroValue = new JournalEntity(firstValue, string.Empty, TreeValuesCache.System);
            var maxValue = new JournalEntity(secondValue, string.Empty, TreeValuesCache.System);
            var keyZero = new JournalKey(guid, DateTime.MinValue.Ticks, first);
            var keyMax = new JournalKey(guid, DateTime.UtcNow.Ticks, second);
            
            _databaseCore.AddJournalValue(keyMax, maxValue);
            _databaseCore.AddJournalValue(keyZero, zeroValue);
        }

        private async Task<List<JournalEntity>> GetJournalValues(Guid guid, DateTime from, DateTime to, RecordType type = RecordType.Changes, int count = 5000) => 
            (await _databaseCore.GetJournalValuesPage(guid, from, to, type, RecordType.Changes, count).Flatten()).Select(x => x.Entity).ToList();

        private List<(JournalKey, JournalEntity)> GenerateJournalEntities(Guid sensorId, int count)
        {
            List<(JournalKey, JournalEntity)> result = new(count);

            for (int i = 0; i < count; i++)
            {
                var key = new JournalKey(sensorId, DateTime.UtcNow.Ticks, RecordType.Changes);
                result.Add((key, new JournalEntity($"TEST_{i}", $"TEST_{i}", $"TEST_{i}")));
            }

            return result;
        }
    }
}