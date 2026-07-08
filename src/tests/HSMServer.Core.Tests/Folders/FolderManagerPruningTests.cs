using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Notifications;
using HSMServer.ServerConfiguration;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Folders
{
    public class FolderManagerPruningTests
    {
        [Fact]
        public async Task TryUpdate_SkipsPruningWhenChatBecomesGlobal()
        {
            var folderId = Guid.NewGuid();
            var chatId = Guid.NewGuid();
            var chat = BuildTelegramChat(chatId);
            chat.Folders.Add(folderId);

            var telegramManager = BuildTelegramManager();
            telegramManager.TryAdd(chatId, chat);
            var slackManager = new SlackDestinationsManager(new Mock<IDatabaseCore>().Object);

            var (folderManager, cacheMock) = BuildFolderManager(telegramManager, slackManager);
            WireEvents(folderManager, telegramManager, slackManager);

            var folder = new FolderModel(BuildFolderEntity(folderId, chatId));
            await folderManager.TryAdd(folder, InitiatorInfo.System);

            var update = new FolderUpdate
            {
                Id = folderId,
                Chats = new HashSet<Guid>(),
                Initiator = InitiatorInfo.System,
            };

            await folderManager.TryUpdate(update);

            cacheMock.Verify(c => c.RemoveChatsFromPoliciesAsync(folderId, It.IsAny<List<Guid>>(), It.IsAny<InitiatorInfo>()), Times.Never);
            Assert.True(telegramManager.TryGetValue(chatId, out var survivingChat));
            Assert.Empty(survivingChat.Folders);
        }

        [Fact]
        public async Task TryUpdate_PrunesWhenChatStillHasOtherFolderBinding()
        {
            var folderId = Guid.NewGuid();
            var otherFolderId = Guid.NewGuid();
            var chatId = Guid.NewGuid();
            var chat = BuildTelegramChat(chatId);
            chat.Folders.Add(folderId);
            chat.Folders.Add(otherFolderId);

            var telegramManager = BuildTelegramManager();
            telegramManager.TryAdd(chatId, chat);
            var slackManager = new SlackDestinationsManager(new Mock<IDatabaseCore>().Object);

            var (folderManager, cacheMock) = BuildFolderManager(telegramManager, slackManager);
            WireEvents(folderManager, telegramManager, slackManager);

            var folder = new FolderModel(BuildFolderEntity(folderId, chatId));
            await folderManager.TryAdd(folder, InitiatorInfo.System);

            var update = new FolderUpdate
            {
                Id = folderId,
                Chats = new HashSet<Guid>(),
                Initiator = InitiatorInfo.System,
            };

            await folderManager.TryUpdate(update);

            cacheMock.Verify(c => c.RemoveChatsFromPoliciesAsync(folderId, It.Is<List<Guid>>(l => l.Contains(chatId)), It.IsAny<InitiatorInfo>()), Times.Once);
        }


        private static TelegramChatsManager BuildTelegramManager()
            => new(new Mock<IDatabaseCore>().Object, new Mock<IUserManager>().Object, new Mock<IServerConfig>().Object);

        private static (FolderManager manager, Mock<ITreeValuesCache> cacheMock) BuildFolderManager(ITelegramChatsManager telegram, ISlackDestinationsManager slack)
        {
            var cacheMock = new Mock<ITreeValuesCache>();
            cacheMock.Setup(c => c.GetProducts()).Returns(new List<ProductModel>());

            var dbMock = new Mock<IDatabaseCore>();
            var userMock = new Mock<IUserManager>();
            userMock.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns(new List<User>());
            var journalMock = new Mock<IJournalService>();

            var manager = new FolderManager(dbMock.Object, cacheMock.Object, userMock.Object, journalMock.Object, telegram, slack);
            return (manager, cacheMock);
        }

        private static void WireEvents(FolderManager manager, TelegramChatsManager telegram, SlackDestinationsManager slack)
        {
            manager.AddFolderToChats += telegram.AddFolderToChats;
            manager.AddFolderToChats += slack.AddFolderToChats;
            manager.RemoveFolderFromChats += telegram.RemoveFolderFromChats;
            manager.RemoveFolderFromChats += slack.RemoveFolderFromChats;
            manager.GetChatName += id => telegram.GetChatName(id) ?? slack.GetSlackDestinationName(id);
        }

        private static TelegramChat BuildTelegramChat(Guid id) =>
            new(new TelegramChatEntity
            {
                Id = id.ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "test-chat",
                ChatId = 123456,
                SendMessages = true,
                Type = (byte)ConnectedChatType.TelegramGroup,
                MessagesAggregationTimeSec = 60,
                AuthorizationTime = DateTime.UtcNow.Ticks,
            });

        private static FolderEntity BuildFolderEntity(Guid folderId, Guid chatId) =>
            new()
            {
                Id = folderId.ToString(),
                DisplayName = "Test folder",
                AuthorId = Guid.NewGuid().ToString(),
                Chats = [chatId.ToByteArray()],
            };
    }
}
