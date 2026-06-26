using System;
using System.Linq;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Notifications;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    [Collection("Database collection")]
    public class SlackDestinationsManagerTests : MonitoringCoreTestsBase<SlackDestinationsFixture>
    {
        public SlackDestinationsManagerTests(SlackDestinationsFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        [Fact]
        public async Task Add_Update_Remove_PersistsThroughLevelDB()
        {
            var db = _databaseCoreManager.DatabaseCore;

            var manager = new SlackDestinationsManager(db);
            await manager.Initialize();
            Assert.Empty(manager.GetValues());

            var destination = new SlackDestination(new SlackAddRequest
            {
                AuthorId = Guid.NewGuid(),
                Name = "alerts",
                WebhookUrl = "https://hooks.slack.com/services/X",
            });

            Assert.True(await manager.TryAdd(destination));
            Assert.Single(manager.GetValues());

            Assert.True(await manager.TryUpdate(new SlackDestinationUpdate
            {
                Id = destination.Id,
                Name = "alerts-renamed",
                WebhookUrl = "https://hooks.slack.com/services/Y",
                SendMessages = false,
            }));

            // Reload from LevelDB -> verifies entity<->model mapping (FromEntity) + persistence.
            var reloaded = new SlackDestinationsManager(db);
            await reloaded.Initialize();

            var loaded = Assert.Single(reloaded.GetValues());
            Assert.Equal("alerts-renamed", loaded.Name);
            Assert.Equal("https://hooks.slack.com/services/Y", loaded.WebhookUrl);
            Assert.False(loaded.SendMessages);

            Assert.True(await reloaded.TryRemove(new RemoveRequest(destination.Id, InitiatorInfo.System)));
            Assert.Empty(reloaded.GetValues());

            var afterRemove = new SlackDestinationsManager(db);
            await afterRemove.Initialize();
            Assert.Empty(afterRemove.GetValues());
        }
    }
}
