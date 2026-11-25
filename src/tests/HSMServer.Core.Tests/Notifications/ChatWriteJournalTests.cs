using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Model.ManagementApi.Chats;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public sealed class ChatWriteJournalFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(ChatWriteJournalTests);
    }

    // #1829: the acceptance pin of the write surface on the REAL storage — a chat written
    // through the management API leaves the journal records of #1828, each naming the
    // token's owner, and none carrying a webhook URL. The service's own suite runs on an
    // in-memory store where no journal exists; this one connects the two seams.
    [Collection("Database collection")]
    public class ChatWriteJournalTests : MonitoringCoreTestsBase<ChatWriteJournalFixture>
    {
        private const string SlackUrl = "https://hooks.slack.com/services/T000/B000/secret_slack";
        private const string MattermostUrl = "https://mattermost.example.com/hooks/secret_mattermost";

        private readonly ChatWriteHarness _world = new();

        private ChatWriteService _service;


        public ChatWriteJournalTests(ChatWriteJournalFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
        }


        // The service over the REAL storage: the chat manager on the test database, writing
        // its journal records through the real journal service. The storage resolves a new
        // row's author to a NAME for its record, so the token's owner is a real user here.
        private async Task<ChatWriteService> Service()
        {
            if (_service is null)
            {
                Assert.True(await _userManager.TryAdd(new User("admin") { Id = ChatWriteHarness.OwnerId, IsAdmin = true }));

                var config = new Mock<IServerConfig>();
                config.SetupGet(c => c.Telegram).Returns(new TelegramConfig { BotName = "hsm_test_bot", BotToken = "test-bot-token" });

                var chats = new ChatsManager(_databaseCoreManager.DatabaseCore, _userManager, config.Object,
                    new JournalAppender(_journalService, TimeProvider.System));

                _service = new ChatWriteService(chats, new ChatAdministrationService(chats, _world.Folders.Object),
                    _world.Authorization.Object, _world.Folders.Object, _world.Users.Object, _world.Usage.Object);
            }

            return _service;
        }


        [Fact]
        public async Task CreateUpdateAndDeleteThroughTheApi_LeaveJournalRecords_NamingTheTokenOwner_WithoutTheWebhookUrls()
        {
            var service = await Service();

            var created = await service.CreateChat(new CreateChatRequestDto { Name = "Ops alerts", SlackWebhookUrl = SlackUrl }, _world.Token);
            Assert.True(created.Success, created.Failure?.Message);

            var updated = await service.UpdateChat(created.Value.Id,
                new UpdateChatRequestDto { Description = "on-call", MattermostWebhookUrl = MattermostUrl }, _world.Token);
            Assert.True(updated.Success, updated.Failure?.Message);

            var removed = await service.DeleteChat(created.Value.Id, _world.Token);
            Assert.True(removed.Success, removed.Failure?.Message);

            // The journal's own read (the one behind the global Journal page), narrowed to the chat.
            var records = _journalService.GetRecords(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMinutes(1))
                .Where(r => r.Key.Id == created.Value.Id)
                .OrderBy(r => r.Key.Time)
                .ToList();

            Assert.Equal(
                [
                    ("Create chat", "chat", "", "Ops alerts"),
                    ("Create chat", "Slack webhook", "not set", "set"),
                    ("Chat update", "Description", "", "on-call"),
                    ("Chat update", "Mattermost webhook", "not set", "set"),
                    ("Remove chat", "chat", "Ops alerts", ""),
                ],
                records.Select(r => (r.Enviroment, r.PropertyName, r.OldValue, r.NewValue)).ToList());

            Assert.All(records, r => Assert.Equal(JournalKinds.Chat, r.Kind));
            Assert.All(records, r => Assert.Equal("User (admin)", r.Initiator));
            Assert.DoesNotContain(records, r => TextOf(r).Contains(SlackUrl) || TextOf(r).Contains(MattermostUrl));
        }


        // Every text cell a record can carry.
        private static string TextOf(JournalRecordModel record) =>
            string.Join("\n", record.Enviroment, record.Initiator, record.PropertyName, record.OldValue, record.NewValue, record.Path);
    }
}
