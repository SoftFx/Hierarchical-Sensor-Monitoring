using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
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
        public async Task RemoveFolderFromChats_RemovesFolderIdAndCallsCache()
        {
            var cache = new Mock<ITreeValuesCache>();
            var manager = BuildManager(cache.Object);
            var destination = BuildDestination();
            manager.TryAdd(destination.Id, destination);
            var folderId = Guid.NewGuid();
            destination.Folders.Add(folderId);

            await manager.RemoveFolderFromChats(folderId, new List<Guid> { destination.Id }, InitiatorInfo.System);

            Assert.DoesNotContain(folderId, destination.Folders);
            cache.Verify(c => c.RemoveChatsFromPoliciesAsync(folderId,
                It.Is<List<Guid>>(list => list.Contains(destination.Id)),
                It.IsAny<InitiatorInfo>()), Times.Once);
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


        private static SlackDestinationsManager BuildManager(ITreeValuesCache cache = null)
            => new(new Mock<IDatabaseCore>().Object, cache ?? new Mock<ITreeValuesCache>().Object);

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
