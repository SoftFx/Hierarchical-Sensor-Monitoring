using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Notifications;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class SlackDestinationsFolderBindingTests
    {
        [Fact]
        public void AddFolderToChats_AddsFolderIdToDestinationFolders()
        {
            var (manager, destinationId) = SeedDestination();
            var folderId = Guid.NewGuid();

            manager.AddFolderToChats(folderId, new List<Guid> { destinationId });

            var destination = manager[destinationId];
            Assert.Contains(folderId, destination.Folders);
        }

        [Fact]
        public void AddFolderToChats_UnknownDestination_SilentlySkipped()
        {
            var manager = BuildManager();
            var unknownId = Guid.NewGuid();

            var ex = Record.Exception(() =>
                manager.AddFolderToChats(Guid.NewGuid(), new List<Guid> { unknownId }));

            Assert.Null(ex);
        }

        [Fact]
        public async Task RemoveFolderFromChats_RemovesFolderIdFromDestinationFolders()
        {
            var manager = BuildManager();
            var destination = BuildDestination();
            manager.TryAdd(destination.Id, destination);
            var folderId = Guid.NewGuid();
            destination.Folders.Add(folderId);

            await manager.RemoveFolderFromChats(folderId, new List<Guid> { destination.Id }, InitiatorInfo.System);

            Assert.DoesNotContain(folderId, destination.Folders);
        }

        [Fact]
        public async Task RemoveFolderFromChats_DoesNotDeleteDestinationEntity()
        {
            var (manager, destinationId) = SeedDestination();
            var folderId = Guid.NewGuid();
            manager[destinationId].Folders.Add(folderId);

            await manager.RemoveFolderFromChats(folderId, new List<Guid> { destinationId }, InitiatorInfo.System);

            Assert.True(manager.TryGetValue(destinationId, out _));
        }

        [Fact]
        public void GetAvailableChats_IncludesZeroFolderDestination()
        {
            var slackManager = BuildManager();
            var dest = BuildDestination();
            slackManager.TryAdd(dest.Id, dest);

            var telegramMock = new Mock<ITelegramChatsManager>();
            telegramMock.Setup(t => t.GetValues()).Returns(new List<TelegramChat>());

            var folderChats = new HashSet<Guid>();

            var available = folderChats.GetAvailableChats(telegramMock.Object, slackManager);

            Assert.Contains(dest.Id, available.Keys);
        }

        [Fact]
        public void GetAvailableChats_BoundDestinationExcludedFromOtherFolder()
        {
            var slackManager = BuildManager();
            var dest = BuildDestination();
            dest.Folders.Add(Guid.NewGuid());
            slackManager.TryAdd(dest.Id, dest);

            var telegramMock = new Mock<ITelegramChatsManager>();
            telegramMock.Setup(t => t.GetValues()).Returns(new List<TelegramChat>());

            var otherFolderChats = new HashSet<Guid>();

            var available = otherFolderChats.GetAvailableChats(telegramMock.Object, slackManager);

            Assert.DoesNotContain(dest.Id, available.Keys);
        }


        private static SlackDestinationsManager BuildManager()
            => new(new Mock<IDatabaseCore>().Object);

        private static SlackDestination BuildDestination() =>
            new(new SlackDestinationEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "alerts-channel",
                WebhookUrl = "https://hooks.slack.com/services/X",
                SendMessages = true,
            });

        private static (SlackDestinationsManager manager, Guid destinationId) SeedDestination()
        {
            var manager = BuildManager();
            var dest = BuildDestination();
            manager.TryAdd(dest.Id, dest);
            return (manager, dest.Id);
        }
    }
}
