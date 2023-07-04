using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Tests.Infrastructure;

namespace HSMDatabase.LevelDB.Tests;

internal static class JournalFactory
{
    internal static JournalEntity BuildJournalEntity() => new(RandomGenerator.GetRandomString());

    internal static JournalKey BuildKey() => new JournalKey(Guid.NewGuid(), RandomGenerator.GetRandomInt());
}