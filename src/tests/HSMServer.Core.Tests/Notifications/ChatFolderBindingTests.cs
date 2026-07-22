using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.Notifications.Telegram.Tokens;
using HSMServer.ServerConfiguration;
using Moq;
using User = HSMServer.Model.Authentication.User;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class ChatFolderBindingTests
    {
        [Fact]
        public async Task RemoveFolderFromChats_DoesNotDeleteChatAtZeroFolders()
        {
            var (manager, chatId) = SeedChat();
            var folderId = Guid.NewGuid();
            manager[chatId].Folders.Add(folderId);

            await manager.RemoveFolderFromChats(folderId, new List<Guid> { chatId }, InitiatorInfo.System);

            Assert.True(manager.TryGetValue(chatId, out var chat));
            Assert.Empty(chat.Folders);
        }

        [Fact]
        public void GetAvailableChats_IncludesZeroFolderChat()
        {
            var (manager, chatId) = SeedChat();

            var folderChats = new HashSet<Guid>();

            var available = folderChats.GetAvailableChats(manager);

            Assert.Contains(chatId, available.Keys);
        }

        [Fact]
        public void GetAvailableChats_BoundChatExcludedFromOtherFolder()
        {
            var manager = BuildManager();
            var chat = BuildChat();
            chat.Folders.Add(Guid.NewGuid());
            manager.TryAdd(chat.Id, chat);

            var otherFolderChats = new HashSet<Guid>();

            var available = otherFolderChats.GetAvailableChats(manager);

            Assert.DoesNotContain(chat.Id, available.Keys);
        }

        [Fact]
        public void AddFolderToChats_AddsFolderIdToChatFolders()
        {
            var (manager, chatId) = SeedChat();
            var folderId = Guid.NewGuid();

            manager.AddFolderToChats(folderId, new List<Guid> { chatId });

            Assert.Contains(folderId, manager[chatId].Folders);
        }

        [Fact]
        public void AddFolderToChats_UnknownChat_SilentlySkipped()
        {
            var manager = BuildManager();
            var unknownId = Guid.NewGuid();

            var ex = Record.Exception(() =>
                manager.AddFolderToChats(Guid.NewGuid(), new List<Guid> { unknownId }));

            Assert.Null(ex);
        }

        // Regression for the #1261 manager merge: RemoveChatHandler used to be two handlers
        // (TelegramChat + SlackDestination). After the merge it is one handler per Chat. The
        // contract — prune the removed chat id from every folder.Chats — must survive the merge.
        [Fact]
        public async Task RemoveChatHandler_PrunesIdFromAllFolders()
        {
            var chats = BuildManager();
            var folderManager = BuildFolderManager(chats);

            var folderId1 = Guid.NewGuid();
            var folderId2 = Guid.NewGuid();
            var chatId = Guid.NewGuid();

            var folder1 = new FolderModel(BuildFolderEntity(folderId1, chatId, "Folder One"));
            var folder2 = new FolderModel(BuildFolderEntity(folderId2, chatId, "Folder Two"));
            await folderManager.TryAdd(folder1, InitiatorInfo.System);
            await folderManager.TryAdd(folder2, InitiatorInfo.System);

            // Simulate NotificationsCenter.ConnectFoldersAndChats hydration
            foreach (var folder in folderManager.GetValues())
                foreach (var id in folder.Chats)
                    if (chats.TryGetValue(id, out var chat))
                        chat.Folders.Add(folder.Id);

            var chatEntity = new ChatEntity
            {
                Id = chatId.ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "test-chat",
                SendMessages = true,
                TelegramType = (byte)ConnectedChatType.TelegramGroup,
                TelegramChatId = 123456,
                MessagesAggregationTimeSec = 60,
            };
            var chatModel = new Chat(chatEntity);
            chats.TryAdd(chatId, chatModel);

            folderManager.RemoveChatHandler(chatModel, InitiatorInfo.System);

            Assert.Empty(folderManager[folderId1].Chats);
            Assert.Empty(folderManager[folderId2].Chats);
        }

        // Regression for #1261 single-subscription wiring: NotificationsCenter wires
        // FolderManager.AddFolderToChats -> IChatsManager.AddFolderToChats once. The previous
        // dual-manager wiring (one Telegram subscriber + one Slack subscriber) would each invoke
        // the chat.Folders mutation. After the merge only one subscriber remains, so a single
        // AddFolderToChats invocation produces exactly one folder id in chat.Folders. (HashSet
        // guards against duplicates, but a regression here would still show up as the event
        // being raised more times than expected in caller wiring.)
        [Fact]
        public void AddFolderToChats_SingleInvocation_AddsFolderOnce()
        {
            var manager = BuildManager();
            var chat = BuildChat();
            manager.TryAdd(chat.Id, chat);
            var folderId = Guid.NewGuid();

            manager.AddFolderToChats(folderId, new List<Guid> { chat.Id });

            Assert.Single(chat.Folders);
            Assert.Contains(folderId, chat.Folders);
        }

        [Fact]
        public void Chat_WithSlackWebhookOnly_HasNoTelegramChatId()
        {
            var (manager, chatId) = SeedSlackOnlyChat();

            Assert.True(manager.TryGetValue(chatId, out var chat));
            Assert.Null(chat.TelegramChatId);
            Assert.False(string.IsNullOrEmpty(chat.SlackWebhookUrl));
        }

        [Fact]
        public void Chat_WithTelegramOnly_HasNoSlackWebhook()
        {
            var (manager, chatId) = SeedChat();

            Assert.True(manager.TryGetValue(chatId, out var chat));
            Assert.NotNull(chat.TelegramChatId);
            Assert.True(string.IsNullOrEmpty(chat.SlackWebhookUrl));
        }

        [Fact]
        public void Chat_WithBothChannels_KeepsTelegramAndSlackFields()
        {
            var manager = BuildManager();
            var chat = BuildMultiChannelChat();
            manager.TryAdd(chat.Id, chat);

            Assert.NotNull(manager[chat.Id].TelegramChatId);
            Assert.False(string.IsNullOrEmpty(manager[chat.Id].SlackWebhookUrl));
        }

        // Regression for the shared-buffer bug surfaced in PR #1265 review:
        // Chat used to expose a single MessageBuilder/ScheduleBuilder/_nextSendMessageTime that
        // both TelegramBot and SlackNotificationChannel wrote to. For a chat with both channels
        // configured, the alert was double-buffered and the first channel to flush bumped the
        // shared timer, so the second channel was skipped. ChannelAccumulator gives each channel
        // its own state — this test pins that contract by draining Telegram and confirming the
        // Slack/Mattermost timers are unchanged. Extended in #1288 to cover the third channel.
        [Fact]
        public void Chat_MultiChannel_AccumulatorsAreIndependent()
        {
            var chat = BuildMultiChannelChat();
            var aggregation = chat.MessagesAggregationTimeSec;

            Assert.NotNull(chat.TelegramAccumulator);
            Assert.NotNull(chat.SlackAccumulator);
            Assert.NotNull(chat.MattermostAccumulator);
            Assert.NotSame(chat.TelegramAccumulator, chat.SlackAccumulator);
            Assert.NotSame(chat.TelegramAccumulator, chat.MattermostAccumulator);
            Assert.NotSame(chat.SlackAccumulator, chat.MattermostAccumulator);

            // All ready to send initially — _nextSendMessageTime defaults to DateTime.MinValue.
            Assert.True(chat.TelegramAccumulator.ShouldSend(aggregation));
            Assert.True(chat.SlackAccumulator.ShouldSend(aggregation));
            Assert.True(chat.MattermostAccumulator.ShouldSend(aggregation));

            // Drain Telegram (mirrors NotificationsCenter flush order [Telegram, Slack, Mattermost]).
            // Even with no buffered alerts, GetNotifications bumps Telegram's timer to the future.
            _ = chat.TelegramAccumulator.GetNotifications(aggregation).ToList();

            // Pre-fix: this was false because Telegram's drain bumped the shared timer.
            // Post-fix: each accumulator owns its own timer, so Slack + Mattermost are still ready.
            Assert.True(chat.SlackAccumulator.ShouldSend(aggregation));
            Assert.True(chat.MattermostAccumulator.ShouldSend(aggregation));

            // Drain Slack too — Mattermost must remain unaffected by either prior drain.
            _ = chat.SlackAccumulator.GetNotifications(aggregation).ToList();
            Assert.True(chat.MattermostAccumulator.ShouldSend(aggregation));
        }


        // #1304: EditChat flow — token carries a ChatId targeting an existing Chat record.
        // TryConnect must bind Telegram in place: TelegramChatId/Type/AuthorizationTime populate
        // on the existing record, and no new Chat is added to storage (no orphan).
        [Fact]
        public async Task TryConnect_WithChatIdToken_BindsTelegramToExistingChat()
        {
            var manager = BuildManager();
            var chat = BuildChat();
            await manager.TryAdd(chat);
            var chatCountBefore = manager.GetValues().Count();

            var token = new InvitationToken(chatId: chat.Id, folderId: Guid.Empty, user: new User("test-user"));
            var message = BuildDirectMessage(chatId: 123456);

            var result = await manager.TryConnect(message, token);

            Assert.Equal(ChatConnectOutcome.ChatBound, result.Outcome);
            Assert.Equal(chat.Name, result.Name);
            Assert.Equal(chatCountBefore, manager.GetValues().Count()); // no orphan created

            var updatedChat = manager[chat.Id];
            Assert.Equal(123456L, updatedChat.TelegramChatId?.Identifier);
            Assert.Equal(ConnectedChatType.TelegramPrivate, updatedChat.TelegramType);
            Assert.NotNull(updatedChat.AuthorizationTime);
        }

        // #1304 regression guard: the legacy folder-scoped flow still creates a brand-new chat
        // bound to the folder. The refactored early branch must not swallow this path.
        [Fact]
        public async Task TryConnect_WithFolderToken_StillCreatesNewChat()
        {
            var manager = BuildManager();
            var folderId = Guid.NewGuid();
            const string folderName = "Production";

            manager.ConnectChatToFolder += (_, id, _) =>
            {
                Assert.Equal(folderId, id);
                return Task.FromResult(folderName);
            };

            var token = new InvitationToken(folderId, new User("test-user"));
            var message = BuildDirectMessage(chatId: 654321);

            var result = await manager.TryConnect(message, token);

            Assert.Equal(ChatConnectOutcome.FolderAdded, result.Outcome);
            Assert.Equal(folderName, result.Name);
            var created = Assert.Single(manager.GetValues());
            Assert.Contains(folderId, created.Folders);
            Assert.Equal(654321L, created.TelegramChatId?.Identifier);
        }

        // #1304 conflict policy — target Chat already bound to a different Telegram chat.
        // Strict refuse: TelegramChatId on the record must not be overwritten.
        [Fact]
        public async Task TryConnect_ChatIdToken_TargetAlreadyBound_ReturnsFailed()
        {
            var manager = BuildManager();
            var chat = new Chat(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "already-bound",
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
                TelegramChatId = 999_999L,
                TelegramType = (byte)ConnectedChatType.TelegramPrivate,
            });
            await manager.TryAdd(chat);

            var token = new InvitationToken(chatId: chat.Id, folderId: Guid.Empty, user: new User("test-user"));
            var message = BuildDirectMessage(chatId: 123_456); // different Telegram chat id

            var result = await manager.TryConnect(message, token);

            Assert.Equal(ChatConnectOutcome.Failed, result.Outcome);
            Assert.Equal(999_999L, manager[chat.Id].TelegramChatId?.Identifier); // unchanged
        }

        // #1304 conflict policy — incoming Telegram chat already owned by another Chat record.
        // Strict refuse: the other record keeps its binding; the target record stays unbound.
        [Fact]
        public async Task TryConnect_ChatIdToken_TelegramChatOwnedByAnotherRecord_ReturnsFailed()
        {
            var manager = BuildManager();

            var owner = new Chat(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "owner",
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
                TelegramChatId = 123_456L,
                TelegramType = (byte)ConnectedChatType.TelegramPrivate,
            });
            await manager.TryAdd(owner);

            var target = BuildSlackOnlyChat(); // no Telegram binding yet
            await manager.TryAdd(target);

            var token = new InvitationToken(chatId: target.Id, folderId: Guid.Empty, user: new User("test-user"));
            var message = BuildDirectMessage(chatId: 123_456); // same Telegram chat as owner

            var result = await manager.TryConnect(message, token);

            Assert.Equal(ChatConnectOutcome.Failed, result.Outcome);
            Assert.Null(manager[target.Id].TelegramChatId); // target untouched
            Assert.Equal(123_456L, manager[owner.Id].TelegramChatId?.Identifier); // owner unchanged
        }


        private static Telegram.Bot.Types.Message BuildDirectMessage(long chatId = 123456) =>
            new()
            {
                Chat = new() { Id = chatId, Type = Telegram.Bot.Types.Enums.ChatType.Private },
                From = new() { Id = 789, Username = "tg-user", FirstName = "Test" },
            };


        private static ChatsManager BuildManager()
            => new(new Mock<IDatabaseCore>().Object, new Mock<IUserManager>().Object, new Mock<IServerConfig>().Object);

        private static Chat BuildChat() =>
            new(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "test-chat",
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
                TelegramChatId = 123456,
                TelegramType = (byte)ConnectedChatType.TelegramGroup,
                AuthorizationTime = DateTime.UtcNow.Ticks,
            });

        private static Chat BuildSlackOnlyChat() =>
            new(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "alerts-channel",
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
                SlackWebhookUrl = "https://hooks.slack.com/services/X",
            });

        private static Chat BuildMultiChannelChat() =>
            new(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "multi-channel-chat",
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
                TelegramChatId = 987654,
                TelegramType = (byte)ConnectedChatType.TelegramGroup,
                AuthorizationTime = DateTime.UtcNow.Ticks,
                SlackWebhookUrl = "https://hooks.slack.com/services/X",
                MattermostWebhookUrl = "https://mattermost.example/hooks/X",
            });

        private static (ChatsManager manager, Guid chatId) SeedChat()
        {
            var manager = BuildManager();
            var chat = BuildChat();
            manager.TryAdd(chat.Id, chat);
            return (manager, chat.Id);
        }

        private static (ChatsManager manager, Guid chatId) SeedSlackOnlyChat()
        {
            var manager = BuildManager();
            var chat = BuildSlackOnlyChat();
            manager.TryAdd(chat.Id, chat);
            return (manager, chat.Id);
        }

        private static FolderManager BuildFolderManager(ChatsManager chats)
        {
            var cacheMock = new Mock<ITreeValuesCache>();
            cacheMock.Setup(c => c.GetProducts()).Returns(new List<ProductModel>());

            var dbMock = new Mock<IDatabaseCore>();
            var userMock = new Mock<IUserManager>();
            userMock.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());
            var journalMock = new Mock<IJournalService>();

            return new FolderManager(dbMock.Object, cacheMock.Object, userMock.Object, journalMock.Object, chats);
        }

        private static FolderEntity BuildFolderEntity(Guid folderId, Guid chatId, string name = "Test folder") =>
            new()
            {
                Id = folderId.ToString(),
                DisplayName = name,
                AuthorId = Guid.NewGuid().ToString(),
                Chats = [chatId.ToByteArray()],
            };
    }
}
