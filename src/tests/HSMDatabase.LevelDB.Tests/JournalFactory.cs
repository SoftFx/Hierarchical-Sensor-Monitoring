using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Tests.Infrastructure;

namespace HSMDatabase.LevelDB.Tests;

internal static class JournalFactory
{
    internal static JournalEntity BuildJournalEntity(string value = null, string path = null, string initiator = null) => 
        new(value ?? RandomGenerator.GetRandomString(), path ??RandomGenerator.GetRandomString(), initiator ?? RandomGenerator.GetRandomString());

    internal static JournalKey BuildKey(Guid? id = null, long? time = null, RecordType? type = null) => 
        new JournalKey(id ?? Guid.NewGuid(), time ?? RandomGenerator.GetRandomInt(), type ?? default);
}