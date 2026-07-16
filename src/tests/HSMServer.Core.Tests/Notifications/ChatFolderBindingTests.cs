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
        // Slack timer is unchanged.
        [Fact]
        public void Chat_MultiChannel_AccumulatorsAreIndependent()
        {
            var chat = BuildMultiChannelChat();
            var aggregation = chat.MessagesAggregationTimeSec;

            Assert.NotNull(chat.TelegramAccumulator);
            Assert.NotNull(chat.SlackAccumulator);
            Assert.NotSame(chat.TelegramAccumulator, chat.SlackAccumulator);

            // Both ready to send initially — _nextSendMessageTime defaults to DateTime.MinValue.
            Assert.True(chat.TelegramAccumulator.ShouldSend(aggregation));
            Assert.True(chat.SlackAccumulator.ShouldSend(aggregation));

            // Drain Telegram (mirrors NotificationsCenter flush order [Telegram, Slack]). Even
            // with no buffered alerts, GetNotifications bumps Telegram's timer to the future.
            _ = chat.TelegramAccumulator.GetNotifications(aggregation).ToList();

            // Pre-fix: this was false because Telegram's drain bumped the shared timer.
            // Post-fix: each accumulator owns its own timer, so Slack is still ready to fire.
            Assert.True(chat.SlackAccumulator.ShouldSend(aggregation));
        }


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
