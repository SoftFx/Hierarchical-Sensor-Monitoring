using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.DatabaseWorkCore;
using HSMDatabase.Settings;
using HSMServer.Core.DataLayer;
using Xunit;

namespace HSMDatabase.LevelDB.Tests
{
    public class JournalDatabaseTest 
    {
        private readonly IDatabaseCore _databaseCore;

        public JournalDatabaseTest()
        {
            _databaseCore = new DatabaseCore(new DatabaseSettings());
        }
        
        [Fact]
        public void Test1()
        {
            var guid = Guid.NewGuid();
            
            var journal = new JournalEntity()
            {
                Key = new Key(guid, 323333),
                Name = "Test1"
            };
            
            _databaseCore.AddJournal(journal);

            var actuals = _databaseCore.GetJournals();

            var actual = _databaseCore.GetJournal(new Key(guid, 123123));

            var a = 1;
        }
    }
}