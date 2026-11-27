using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Model.Authentication;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.Notifications.Telegram.Tokens;
using HSMServer.ServerConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public sealed class ChatJournalFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(ChatJournalTests);
    }

    // #1828: every chat write — the web flows' engine, the delete action, the Telegram /start
    // binding, the bot's own cleanup — goes through ChatsManager, and each leaves `chat` records
    // in the journal, one per changed setting, readable through the journal service. The kind is
    // journal-visible only: a chat holds webhooks, and no secret may sit in a record.
    public class ChatJournalTests : MonitoringCoreTestsBase<ChatJournalFixture>
    {
        private const string SlackUrl = "https://hooks.slack.com/services/T000/B000/XXXXXXXXXXXXXXXXXXXXXXXX";
        private const string MattermostUrl = "https://mattermost.example.com/hooks/zzzzzzzzzzzzzzzzzzzzzzzzzz";
        private const long TelegramId = 555_000_111L;

        private readonly Mock<IServerConfig> _config = new();

        private readonly ChatsManager _chats;


        public ChatJournalTests(ChatJournalFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _config.SetupGet(c => c.Telegram).Returns(new TelegramConfig { BotName = "hsm_test_bot", BotToken = "test-bot-token" });

            _chats = BuildChats(_journalService);
        }


        [Fact]
        public async Task TryUpdate_ChangedName_LeavesAChatRecord_WithOldAndNewValues()
        {
            var chat = await AddChatAsync(name: "Ops");
            var writtenBefore = RecordsOf(chat.Id).Count;

            var updated = await _chats.TryUpdate(new ChatUpdate
            {
                Id = chat.Id,
                Name = "Ops alerts",
                Initiator = InitiatorInfo.AsUser("admin"),
            });

            Assert.True(updated);
            var record = Assert.Single(RecordsOf(chat.Id).Skip(writtenBefore));
            Assert.Equal(JournalKinds.Chat, record.Kind);
            Assert.Equal("Name", record.PropertyName);
            Assert.Equal("Ops", record.OldValue);
            Assert.Equal("Ops alerts", record.NewValue);
            Assert.Equal("Ops alerts", record.Path);
            Assert.Equal("User (admin)", record.Initiator);
            Assert.Equal(JournalOperations.Update, JournalOperations.Of(record));
            Assert.Null(record.ChangeSetId);
        }


        [Fact]
        public async Task TryUpdate_ChangedSettings_LeavesOneRecordPerSetting()
        {
            var chat = await AddChatAsync(description: "On-call");
            var writtenBefore = RecordsOf(chat.Id).Count;

            var updated = await _chats.TryUpdate(new ChatUpdate
            {
                Id = chat.Id,
                Description = "Escalations",
                SendMessages = false,
                MessagesAggregationTimeSec = 300,
                Initiator = InitiatorInfo.AsUser("admin"),
            });

            Assert.True(updated);
            Assert.Equal(
                [
                    ("Description", "On-call", "Escalations"),
                    ("Enable messages", "True", "False"),
                    ("Messages delay", "60 sec", "300 sec"),
                ],
                ChangesOf(RecordsOf(chat.Id).Skip(writtenBefore)));
        }

        // The journal addresses a record by (entity, time, record type). With a clock that
        // stands still every record would land on the address of the one before it — inside
        // one write and across the writes of one chat — and only the last would survive.
        [Fact]
        public async Task WritesWhileTheClockStandsStill_KeepEveryRecord()
        {
            var chats = BuildChats(_journalService, new FrozenClock(DateTime.UtcNow));
            var chat = new Chat(BuildChatEntity(name: "Ops") with
            {
                SlackWebhookUrl = SlackUrl,
                TelegramChatId = TelegramId,
                TelegramType = (byte)ConnectedChatType.TelegramGroup,
                AuthorizationTime = DateTime.UtcNow.Ticks,
            });

            Assert.True(await chats.TryAdd(chat));
            Assert.True(await chats.TryUpdate(new ChatUpdate { Id = chat.Id, Name = "Ops alerts", SendMessages = false }));
            Assert.True(await chats.TryRemove(new RemoveRequest(chat.Id, InitiatorInfo.System)));

            var records = RecordsOf(chat.Id);
            Assert.Equal(
                ["chat", "Slack webhook", "Telegram binding", "Name", "Enable messages", "chat"],
                records.Select(r => r.PropertyName));
            Assert.Equal(records.Count, records.Select(r => (r.Key.Time, r.Key.Type)).Distinct().Count());
        }

        // A webhook is a secret: its record says which channel changed and how, never the URL.
        [Theory]
        [InlineData(true, "Slack webhook")]
        [InlineData(false, "Mattermost webhook")]
        public async Task TryUpdate_WebhookSetChangedAndRemoved_ReadsAsStates_NeverTheUrl(bool isSlack, string channel)
        {
            var chat = await AddChatAsync();
            var writtenBefore = RecordsOf(chat.Id).Count;

            await UpdateWebhookAsync(chat.Id, isSlack, "https://hooks.example.com/services/first");
            await UpdateWebhookAsync(chat.Id, isSlack, "https://hooks.example.com/services/second");
            await ClearWebhookAsync(chat.Id, isSlack);

            Assert.Equal(
                [
                    (channel, "not set", "set"),
                    (channel, "set", "changed"),
                    (channel, "set", "removed"),
                ],
                ChangesOf(RecordsOf(chat.Id).Skip(writtenBefore)));
            Assert.DoesNotContain(RecordsOf(chat.Id), r => TextOf(r).Contains("hooks.example.com"));
        }

        // The Telegram chat id is not written either: for a direct chat it is a person's
        // Telegram user id.
        [Fact]
        public async Task TryUpdate_TelegramBoundAndCleared_ReadsAsStates_NeverTheChatId()
        {
            var chat = await AddChatAsync();
            chat.TelegramType = ConnectedChatType.TelegramGroup;
            var writtenBefore = RecordsOf(chat.Id).Count;

            var bound = await _chats.TryUpdate(new ChatUpdate { Id = chat.Id, TelegramChatId = TelegramId });
            var cleared = await _chats.TryUpdate(new ChatUpdate { Id = chat.Id, ClearTelegramBinding = true });

            Assert.True(bound);
            Assert.True(cleared);
            Assert.Equal(
                [
                    ("Telegram binding", "not bound", "bound (group)"),
                    ("Telegram binding", "bound (group)", "removed"),
                ],
                ChangesOf(RecordsOf(chat.Id).Skip(writtenBefore)));
            Assert.DoesNotContain(RecordsOf(chat.Id), r => TextOf(r).Contains(TelegramId.ToString()));
        }

        // The bot mirrors every Telegram chat's title and description on each of its starts,
        // and a group that became a supergroup is moved to its new id: neither is an
        // operator's change, and journaling them would drown the journal.
        [Fact]
        public async Task TryUpdate_BotTitleSyncOrSupergroupMove_LeavesNoRecord()
        {
            var chat = await AddChatAsync();
            chat.TelegramType = ConnectedChatType.TelegramGroup;
            await _chats.TryUpdate(new ChatUpdate { Id = chat.Id, TelegramChatId = TelegramId });
            var writtenBefore = RecordsOf(chat.Id).Count;

            await _chats.TryUpdate(new ChatUpdate { Id = chat.Id, TelegramChatTitle = "On-call room", TelegramChatDescription = "Alerts" });
            await _chats.MigrateToSupergroup(TelegramId, TelegramId + 1);

            Assert.Equal(writtenBefore, RecordsOf(chat.Id).Count);
            Assert.Equal(TelegramId + 1, chat.TelegramChatId.Identifier);
            Assert.Equal("On-call room", chat.TelegramChatTitle);
        }

        // Re-sending a value the chat already holds changes nothing, and so leaves nothing.
        [Fact]
        public async Task TryUpdate_NothingChanged_LeavesNoRecord()
        {
            var chat = await AddChatAsync(description: "On-call");
            await _chats.TryUpdate(new ChatUpdate { Id = chat.Id, SlackWebhookUrl = SlackUrl });
            var writtenBefore = RecordsOf(chat.Id).Count;

            var updated = await _chats.TryUpdate(new ChatUpdate
            {
                Id = chat.Id,
                Name = "Ops",
                Description = "On-call",
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
                SlackWebhookUrl = SlackUrl,
            });

            Assert.True(updated);
            Assert.Equal(writtenBefore, RecordsOf(chat.Id).Count);
        }

        [Fact]
        public async Task TryUpdate_UnknownChat_LeavesNoRecord()
        {
            var unknownId = Guid.NewGuid();

            Assert.False(await _chats.TryUpdate(new ChatUpdate { Id = unknownId, Name = "Ghost" }));

            Assert.Empty(RecordsOf(unknownId));
        }

        // The author of the row is the initiator of its creation: the web flow passes the
        // signed-in user as the author, the /start flow the user who issued the invitation.
        [Fact]
        public async Task TryAdd_LeavesACreateChatRecord_NamingTheAuthor()
        {
            var admin = new User("admin");
            Assert.True(await _userManager.TryAdd(admin));

            var chat = new Chat(BuildChatEntity(name: "Ops") with { Author = admin.Id.ToByteArray() });

            Assert.True(await _chats.TryAdd(chat));

            var record = Assert.Single(RecordsOf(chat.Id));
            Assert.Equal(JournalKinds.Chat, record.Kind);
            Assert.Equal("Create chat", record.Enviroment);
            Assert.Equal(("chat", "", "Ops"), (record.PropertyName, record.OldValue, record.NewValue));
            Assert.Equal("Ops", record.Path);
            Assert.Equal("User (admin)", record.Initiator);
            Assert.Equal(JournalOperations.Create, JournalOperations.Of(record));
        }

        // #1932: a read returns the author's name from the row, and a row used to get it at
        // server start only — a chat created while the server ran read with no author until
        // the next restart. The name is resolved with the add.
        [Fact]
        public async Task TryAdd_OfAKnownAuthor_ReadsWithTheAuthorsNameAtOnce()
        {
            var admin = new User("admin");
            Assert.True(await _userManager.TryAdd(admin));

            var chat = new Chat(BuildChatEntity(name: "Ops") with { Author = admin.Id.ToByteArray() });
            Assert.True(await _chats.TryAdd(chat));

            var dto = HSMServer.Model.ManagementApi.Chats.ChatDtoMapper.ToDto(_chats[chat.Id], _ => true, includeDetail: true);
            Assert.Equal("admin", dto.Author);
        }

        // A row whose author is not a user of this server has no name to read.
        [Fact]
        public async Task TryAdd_OfAnUnknownAuthor_ReadsWithNoAuthor()
        {
            var chat = await AddChatAsync(name: "Ops");

            var dto = HSMServer.Model.ManagementApi.Chats.ChatDtoMapper.ToDto(_chats[chat.Id], _ => true, includeDetail: true);
            Assert.Null(dto.Author);
        }

        [Fact]
        public async Task TryAdd_ChatCreatedWithItsChannels_LeavesARecordPerChannel()
        {
            var chat = new Chat(BuildChatEntity(name: "Ops") with
            {
                SlackWebhookUrl = SlackUrl,
                MattermostWebhookUrl = MattermostUrl,
                TelegramChatId = TelegramId,
                TelegramType = (byte)ConnectedChatType.TelegramGroup,
                AuthorizationTime = DateTime.UtcNow.Ticks,
            });

            Assert.True(await _chats.TryAdd(chat));

            var records = RecordsOf(chat.Id);
            Assert.Equal(
                [
                    ("chat", "", "Ops"),
                    ("Slack webhook", "not set", "set"),
                    ("Mattermost webhook", "not set", "set"),
                    ("Telegram binding", "not bound", "bound (group)"),
                ],
                ChangesOf(records));
            Assert.All(records, r => Assert.Equal(JournalOperations.Create, JournalOperations.Of(r)));
            Assert.DoesNotContain(records, r => TextOf(r).Contains(SlackUrl) || TextOf(r).Contains(MattermostUrl));
        }

        [Fact]
        public async Task TryRemove_LeavesARemoveChatRecord_NamingTheInitiator()
        {
            var chat = await AddChatAsync(name: "Ops");

            Assert.True(await _chats.TryRemove(new RemoveRequest(chat.Id, InitiatorInfo.AsUser("admin"))));

            var record = RecordsOf(chat.Id)[^1];
            Assert.Equal(JournalKinds.Chat, record.Kind);
            Assert.Equal("Remove chat", record.Enviroment);
            Assert.Equal(("chat", "Ops", ""), (record.PropertyName, record.OldValue, record.NewValue));
            Assert.Equal("Ops", record.Path);
            Assert.Equal("User (admin)", record.Initiator);
            Assert.Equal(JournalOperations.Delete, JournalOperations.Of(record));
        }

        [Fact]
        public async Task TryRemove_UnknownChat_LeavesNoRecord()
        {
            var unknownId = Guid.NewGuid();

            Assert.False(await _chats.TryRemove(new RemoveRequest(unknownId, InitiatorInfo.System)));

            Assert.Empty(RecordsOf(unknownId));
        }

        // The pin of #1828: every path that leaves chat records, driven with secrets in the
        // chat, and not one record carries a webhook URL or the Telegram chat id.
        [Fact]
        public async Task EveryRecordProducingChatWrite_NeverWritesAWebhookUrlOrTheTelegramChatId()
        {
            const string firstSlack = "https://hooks.slack.com/services/T000/B000/secret_slack_one";
            const string secondSlack = "https://hooks.slack.com/services/T000/B000/secret_slack_two";
            const string mattermost = "https://mattermost.example.com/hooks/secret_mattermost";

            var chat = new Chat(BuildChatEntity(name: "Ops") with { SlackWebhookUrl = firstSlack, MattermostWebhookUrl = mattermost });
            Assert.True(await _chats.TryAdd(chat));

            Assert.True(await _chats.TryUpdate(new ChatUpdate { Id = chat.Id, SlackWebhookUrl = secondSlack }));
            Assert.True(await _chats.TryUpdate(new ChatUpdate { Id = chat.Id, ClearMattermostWebhook = true }));

            var token = new InvitationToken(chatId: chat.Id, folderId: Guid.Empty, user: new User("admin"));
            Assert.Equal(ChatConnectOutcome.ChatBound, (await _chats.TryConnect(BuildDirectMessage(TelegramId), token)).Outcome);
            Assert.True(await _chats.TryUpdate(new ChatUpdate { Id = chat.Id, ClearTelegramBinding = true }));

            Assert.True(await _chats.TryRemove(new RemoveRequest(chat.Id, InitiatorInfo.AsUser("admin"))));

            // The whole life of the chat, as the journal holds it.
            var records = RecordsOf(chat.Id);
            Assert.Equal(
                [
                    ("chat", "", "Ops"),
                    ("Slack webhook", "not set", "set"),
                    ("Mattermost webhook", "not set", "set"),
                    ("Slack webhook", "set", "changed"),
                    ("Mattermost webhook", "set", "removed"),
                    ("Telegram binding", "not bound", "bound (direct)"),
                    ("Telegram binding", "bound (direct)", "removed"),
                    ("chat", "Ops", ""),
                ],
                ChangesOf(records));

            var secrets = new[] { firstSlack, secondSlack, mattermost, TelegramId.ToString() };
            Assert.DoesNotContain(records, r => secrets.Any(secret => TextOf(r).Contains(secret)));
        }

        // The /start command is typed by the person in Telegram, but the binding belongs to
        // the user who issued the invitation.
        [Fact]
        public async Task TryConnect_BindsTelegram_NamingTheUserWhoIssuedTheInvitation()
        {
            var chat = await AddChatAsync(name: "Ops");
            var writtenBefore = RecordsOf(chat.Id).Count;
            var token = new InvitationToken(chatId: chat.Id, folderId: Guid.Empty, user: new User("admin"));

            var result = await _chats.TryConnect(BuildGroupMessage(TelegramId), token);

            Assert.Equal(ChatConnectOutcome.ChatBound, result.Outcome);
            var record = Assert.Single(RecordsOf(chat.Id).Skip(writtenBefore));
            Assert.Equal(("Telegram binding", "not bound", "bound (group)"), (record.PropertyName, record.OldValue, record.NewValue));
            Assert.Equal("User (admin)", record.Initiator);
        }

        // The Add-chat form holds a chat id before any row exists; the /start against its
        // invitation creates the row.
        [Fact]
        public async Task TryConnect_PreAllocatedChat_LeavesCreateRecords_NamingTheInvitationIssuer()
        {
            var chatId = Guid.NewGuid();
            var token = new InvitationToken(chatId: chatId, folderId: Guid.Empty, user: new User("admin"));

            var result = await _chats.TryConnect(BuildDirectMessage(TelegramId), token);

            Assert.Equal(ChatConnectOutcome.ChatBound, result.Outcome);
            var records = RecordsOf(chatId);
            Assert.Equal(
                [
                    ("chat", "", "tg-user"),
                    ("Telegram binding", "not bound", "bound (direct)"),
                ],
                ChangesOf(records));
            Assert.All(records, r => Assert.Equal("User (admin)", r.Initiator));
        }

        // The folder invitation of the folder's Chats tab creates a brand-new chat.
        [Fact]
        public async Task TryConnect_FolderInvitation_LeavesACreateRecord_NamingTheInvitationIssuer()
        {
            _chats.ConnectChatToFolder += (_, _, _) => Task.FromResult("On-call folder");
            var token = new InvitationToken(Guid.NewGuid(), new User("admin"));

            var result = await _chats.TryConnect(BuildGroupMessage(TelegramId), token);

            Assert.Equal(ChatConnectOutcome.FolderAdded, result.Outcome);
            var chat = _chats.GetChatByChatId(new Telegram.Bot.Types.ChatId(TelegramId));
            var records = RecordsOf(chat.Id);
            Assert.Equal(
                [
                    ("chat", "", "On-call room"),
                    ("Telegram binding", "not bound", "bound (group)"),
                ],
                ChangesOf(records));
            Assert.All(records, r => Assert.Equal("User (admin)", r.Initiator));
        }

        // The journal is appended after the write landed: a storage failure of the journal
        // is logged and must not fail or undo the chat write (ADR-0010).
        [Fact]
        public async Task TryUpdate_AndTryAdd_WhenTheJournalWriteFails_StillLandTheChat()
        {
            var db = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false) { ShouldFailJournalWrite = _ => true };
            var chats = BuildChats(db, new JournalService(db));
            var chat = new Chat(BuildChatEntity(name: "Ops"));

            var added = await chats.TryAdd(chat);
            var updated = await chats.TryUpdate(new ChatUpdate { Id = chat.Id, Name = "Ops alerts" });

            Assert.True(added);
            Assert.True(updated);
            Assert.Equal("Ops alerts", chats[chat.Id].Name);
        }


        private Task<bool> UpdateWebhookAsync(Guid chatId, bool isSlack, string url) => _chats.TryUpdate(new ChatUpdate
        {
            Id = chatId,
            SlackWebhookUrl = isSlack ? url : null,
            MattermostWebhookUrl = isSlack ? null : url,
        });

        private Task<bool> ClearWebhookAsync(Guid chatId, bool isSlack) => _chats.TryUpdate(new ChatUpdate
        {
            Id = chatId,
            ClearSlackWebhook = isSlack ? true : null,
            ClearMattermostWebhook = isSlack ? null : true,
        });

        // Every text cell a record can carry.
        private static string TextOf(JournalRecordModel record) =>
            string.Join("\n", record.Enviroment, record.Initiator, record.PropertyName, record.OldValue, record.NewValue, record.Path);


        private static List<(string Property, string OldValue, string NewValue)> ChangesOf(IEnumerable<JournalRecordModel> records) =>
            records.Select(r => (r.PropertyName, r.OldValue, r.NewValue)).ToList();

        private ChatsManager BuildChats(IJournalService journal, TimeProvider time = null) =>
            BuildChats(_databaseCoreManager.DatabaseCore, journal, time);

        private ChatsManager BuildChats(IDatabaseCore database, IJournalService journal, TimeProvider time = null) =>
            new(database, _userManager, _config.Object, new JournalAppender(journal, time ?? TimeProvider.System));

        private static Telegram.Bot.Types.Message BuildDirectMessage(long chatId) => BuildMessage(chatId, Telegram.Bot.Types.Enums.ChatType.Private);

        private static Telegram.Bot.Types.Message BuildGroupMessage(long chatId) => BuildMessage(chatId, Telegram.Bot.Types.Enums.ChatType.Group);

        private static Telegram.Bot.Types.Message BuildMessage(long chatId, Telegram.Bot.Types.Enums.ChatType type) => new()
        {
            Chat = new() { Id = chatId, Type = type, Title = "On-call room" },
            From = new() { Id = 789, Username = "tg-user", FirstName = "Test" },
        };

        private async Task<Chat> AddChatAsync(string name = "Ops", string description = null)
        {
            var chat = new Chat(BuildChatEntity(name, description));

            Assert.True(await _chats.TryAdd(chat));
            return chat;
        }

        // A chat as the create flow stores it: messages on, the default one-minute delay.
        private static ChatEntity BuildChatEntity(string name, string description = null) => new()
        {
            Id = Guid.NewGuid().ToByteArray(),
            Author = Guid.NewGuid().ToByteArray(),
            CreationDate = DateTime.UtcNow.Ticks,
            Name = name,
            Description = description,
            SendMessages = true,
            MessagesAggregationTimeSec = 60,
        };

        // A clock that stands still: every record of a write is stamped with the same "now".
        private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        // The journal's own read (the one behind the global Journal page), narrowed to one chat.
        private List<JournalRecordModel> RecordsOf(Guid entityId) =>
            _journalService.GetRecords(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMinutes(1))
                .Where(r => r.Key.Id == entityId)
                .OrderBy(r => r.Key.Time)
                .ToList();
    }
}
