using HSMServer.Core.Journal;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.Journal
{
    // #1828: the kinds list is public contract — the journal API and the MCP journal tool
    // descriptions enumerate it by hand — so the full list is pinned, with the split between
    // the rollbackable kinds (their writes carry undo groups) and the journal-visible-only ones.
    public class JournalKindsTests
    {
        [Fact]
        public void All_IsTheFullListOfKinds()
        {
            Assert.Equal(
                [
                    "sensorDataPolicies", "sensorTtlPolicies", "productTtlPolicies", "alertTemplate", "alertSchedule",
                    "nodeSettings", "treeCrud", "folder", "chat", "serverSettings",
                ],
                JournalKinds.All);
        }

        [Fact]
        public void Rollbackable_IsTheUndoableSubset()
        {
            Assert.Equal(
                ["sensorDataPolicies", "sensorTtlPolicies", "productTtlPolicies", "alertTemplate", "alertSchedule"],
                JournalKinds.Rollbackable);

            Assert.All(JournalKinds.Rollbackable, kind => Assert.Contains(kind, JournalKinds.All));
        }

        // A chat holds webhooks and the bot settings hold the token: secrets cannot sit in
        // before/after images, so these two kinds are never rollbackable.
        [Theory]
        [InlineData(JournalKinds.Chat)]
        [InlineData(JournalKinds.ServerSettings)]
        public void ChatAndServerSettings_AreJournalVisibleOnly(string kind)
        {
            Assert.Contains(kind, JournalKinds.All);
            Assert.DoesNotContain(kind, JournalKinds.Rollbackable);
            Assert.False(JournalKinds.IsRollbackable(kind));
        }
    }
}
