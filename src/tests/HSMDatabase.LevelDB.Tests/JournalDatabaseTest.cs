using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.DatabaseTests;
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
        public void Test1()
        {
            var guid = Guid.NewGuid();
            var key = new Key(guid, 323333);
            var journal = new JournalEntity()
            {
                Key = key,
                Name = "Test1"
            };
            
            _databaseCore.AddJournal(journal);

            var actuals = _databaseCore.GetJournals();

            var actual = _databaseCore.GetJournal(key);

            Assert.Equal(journal.Key, actual.Key);
            Assert.Equal(journal.Name, actual.Name);
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

                var actual = _databaseCore.GetJournal(expectedJournal.Key);

                Assert.Equal(expectedJournal.Key, actual.Key);
                Assert.Equal(expectedJournal.Name, actual.Name);
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
                _databaseCore.RemoveJournal(expectedJournal.Key);

                Assert.Null(_databaseCore.GetJournal(expectedJournal.Key));
            }

            Assert.Empty(_databaseCore.GetJournals());
        }
    }
}