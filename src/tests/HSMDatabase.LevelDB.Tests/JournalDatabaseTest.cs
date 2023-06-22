using System.Text;
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
        public async void Test2()
        {
            var guid = Guid.NewGuid();
            var key = new Key(guid, 599506272170000000);
            var journal = new JournalEntity()
            {
                Id = key,
                Value = "Test1"
            };
            
            var journal2 = new JournalEntity()
            {
                Id = new Key(guid, 599509728340000000),
                Value = "Test2"
            };
            
            var journal3 = new JournalEntity()
            {
                Id = new Key(guid, 599527872510000000),
                Value = "Test3"
            };

            var start = new DateTime(599506272170000000).AddMilliseconds(-100);
            var end = new DateTime(599527872510000000);
            
            _databaseCore.AddJournalValue(journal2);
            _databaseCore.AddJournalValue(journal3);
            _databaseCore.AddJournalValue(journal);
            
            var pages = await _databaseCore.GetJournalValuesPage(guid, DateTime.MinValue, DateTime.MaxValue, 50000).Flatten();
            
            foreach (var item in pages)
            {
                var b = JsonSerializer.Deserialize<JournalEntity>(Encoding.UTF8.GetString(item));
                var asd = 1;
            }
            
            
            var c = 1;
        }
        
        [Fact]
        public void Test1()
        {
            var guid = Guid.NewGuid();
            var key = new Key(guid, 323333);
            var journal = new JournalEntity()
            {
                Id = key,
                Value = "Test1"
            };
            
            _databaseCore.AddJournal(journal);

            var actuals = _databaseCore.GetJournals();

            var actual = _databaseCore.GetJournal(key);

            Assert.Equal(journal.Id, actual.Id);
            Assert.Equal(journal.Value, actual.Value);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        public void AddSeveralJournalsTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var expectedJournal = JournalFactory.BuildJournalEntity();
                _databaseCore.AddJournal(expectedJournal);

                var actual = _databaseCore.GetJournal(expectedJournal.Id);

                Assert.Equal(expectedJournal.Id, actual.Id);
                Assert.Equal(expectedJournal.Value, actual.Value);
            }
        }
        [Theory]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        public void RemoveJournalTest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var expectedJournal = JournalFactory.BuildJournalEntity();

                _databaseCore.AddJournal(expectedJournal);
                _databaseCore.RemoveJournal(expectedJournal.Id);

                Assert.Null(_databaseCore.GetJournal(expectedJournal.Id));
            }

            Assert.Empty(_databaseCore.GetJournals());
        }
    }
}