using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Notifications;
using HSMServer.ServerConfiguration;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class TelegramChatsFolderBindingTests
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

            var slackMock = new Mock<ISlackDestinationsManager>();
            slackMock.Setup(s => s.GetValues()).Returns(new List<SlackDestination>());

            var folderChats = new HashSet<Guid>();

            var available = folderChats.GetAvailableChats(manager, slackMock.Object);

            Assert.Contains(chatId, available.Keys);
        }


        private static TelegramChatsManager BuildManager()
            => new(new Mock<IDatabaseCore>().Object, new Mock<IUserManager>().Object, new Mock<IServerConfig>().Object);

        private static TelegramChat BuildChat() =>
            new(new TelegramChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "test-chat",
                ChatId = 123456,
                SendMessages = true,
                Type = (byte)ConnectedChatType.TelegramGroup,
                MessagesAggregationTimeSec = 60,
                AuthorizationTime = DateTime.UtcNow.Ticks,
            });

        private static (TelegramChatsManager manager, Guid chatId) SeedChat()
        {
            var manager = BuildManager();
            var chat = BuildChat();
            manager.TryAdd(chat.Id, chat);
            return (manager, chat.Id);
        }
    }
}
