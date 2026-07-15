using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Moq;
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

        [Fact]
        public void Chat_WithSlackWebhookOnly_DeliversViaSlackChannel()
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
    }
}
