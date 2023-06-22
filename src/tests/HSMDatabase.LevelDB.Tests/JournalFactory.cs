using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Tests.Infrastructure;

namespace HSMDatabase.LevelDB.Tests;

internal static class JournalFactory
{
    internal static JournalEntity BuildJournalEntity() =>
        new()
        {
            Id = BuildKey(),
            Name = RandomGenerator.GetRandomString()
        };

    internal static Key BuildKey() => new Key(Guid.NewGuid(), RandomGenerator.GetRandomInt());
}