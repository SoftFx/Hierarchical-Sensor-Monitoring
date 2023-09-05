using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;

namespace HSMDatabase.LevelDB.Tests.JournalDBTests;

internal static class JournalFactory
{
    internal static JournalRecordEntity GetEntity(string value = null, string path = null, string initiator = null) => new()
    {
        OldValue = value ?? RandomGenerator.GetRandomString(),
        Path = path ?? RandomGenerator.GetRandomString(),
        Initiator = initiator ?? RandomGenerator.GetRandomString(),
    };


    internal static JournalKey GetKey(Guid? id = null, long? time = null, RecordType? type = null) =>
        new(id ?? Guid.NewGuid(), time ?? RandomGenerator.GetRandomInt(), type ?? default);


    internal static JournalRecordModel GetRecord(Guid id, InitiatorInfo initiator = null) => new(id, initiator)
    {
        PropertyName = RandomGenerator.GetRandomString(),
        OldValue = RandomGenerator.GetRandomString(),
        NewValue = RandomGenerator.GetRandomString(),
        Path = RandomGenerator.GetRandomString(),
    };
}