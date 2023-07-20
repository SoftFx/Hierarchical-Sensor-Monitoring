using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Tests.Infrastructure;

namespace HSMDatabase.LevelDB.Tests;

internal static class JournalFactory
{
    internal static JournalRecordEntity BuildJournalEntity(string value = null, string path = null, string initiator = null) => new()
    {
        OldValue = value ?? RandomGenerator.GetRandomString(),
        Path = path ?? RandomGenerator.GetRandomString(),
        Initiator = initiator ?? RandomGenerator.GetRandomString(),
    };


    internal static JournalKey BuildKey(Guid? id = null, long? time = null, RecordType? type = null) =>
        new(id ?? Guid.NewGuid(), time ?? RandomGenerator.GetRandomInt(), type ?? default);
}