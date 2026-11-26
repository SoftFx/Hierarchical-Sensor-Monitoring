using Xunit;

namespace HSMDatabase.LevelDB.Tests.LargeFileTests
{
    // Runs LargeFileStoreKillTests after every parallel test, with nothing beside it (see the reason on the class).
    [CollectionDefinition(LargeFileStoreKillTests.Name, DisableParallelization = true)]
    public sealed class LargeFileStoreKillCollection;
}
