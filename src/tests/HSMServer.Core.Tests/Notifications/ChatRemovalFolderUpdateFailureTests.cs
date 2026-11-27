using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Folders;
using HSMServer.Notifications.Chats;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // The folder updates a chat's removal runs are fire-and-forget from a synchronous event.
    // The update is an async method, so a failure of it — of its synchronous part as much as
    // of what it awaits — used to vanish in the discarded task: nothing logged, nothing
    // observed, the folder left referring to a chat that no longer exists. It is now logged
    // and stays with its folder: the other folders and the chat's own removal record are
    // not affected either way.
    [Collection("NLog isolated")]
    public sealed class ChatRemovalFolderUpdateFailureTests : IDisposable
    {
        private readonly MemoryTarget _memory = new() { Layout = "${message}" };
        private readonly LoggingConfiguration _previous = LogManager.Configuration;

        public ChatRemovalFolderUpdateFailureTests() =>
            LogManager.Setup().LoadConfiguration(cfg => cfg.ForLogger().WriteTo(_memory));

        public void Dispose() => LogManager.Configuration = _previous;


        private string Logs => string.Join('\n', _memory.Logs);

        // Two folders bound to both chats; the first one is the one the test makes fail.
        private sealed record Scene(ChatFolderBindingTests.World World, Chat Deleted, Chat Kept, FolderModel Failing, FolderModel Other,
            List<JournalRecordModel> ChatRecords);

        private static async Task<Scene> BuildSceneAsync()
        {
            var chatRecords = new List<JournalRecordModel>();
            var chatJournal = new Mock<IJournalService>();
            chatJournal.Setup(j => j.AddRecord(It.IsAny<JournalRecordModel>())).Callback<JournalRecordModel>(chatRecords.Add);

            var world = ChatFolderBindingTests.BuildWorld(chatJournal.Object);
            var (deleted, kept) = await world.AddChatsAsync("gone-chat", "kept-chat");
            var failing = await world.AddFolderAsync("Failing", bound: [deleted.Id, kept.Id]);
            var other = await world.AddFolderAsync("Other", bound: [deleted.Id, kept.Id]);

            return new(world, deleted, kept, failing, other, chatRecords);
        }


        // The folder's stored update fails — the database throws. Seen red against the
        // handler that discarded the task: nothing was logged then.
        [Fact]
        public async Task TryRemove_FolderUpdateThatThrows_IsLogged_AndTheRemovalGoesOn()
        {
            var scene = await BuildSceneAsync();
            scene.World.Database.Setup(d => d.UpdateFolder(It.Is<FolderEntity>(e => e.Id == scene.Failing.Id.ToString())))
                .Throws(new InvalidOperationException("database unavailable"));
            scene.ChatRecords.Clear();

            Assert.True(await scene.World.Chats.TryRemove(new RemoveRequest(scene.Deleted.Id, InitiatorInfo.System)));

            // The failed folder is named in the log, with the chat it still refers to, and
            // the failure cost neither the chat its own removal record nor the other folder
            // its cleanup.
            Assert.Contains("Failing", Logs);
            Assert.Contains("gone-chat", Logs);

            Assert.False(scene.World.Chats.TryGetValue(scene.Deleted.Id, out _));
            Assert.Equal([scene.Kept.Id], scene.Other.Chats);
            Assert.Equal([("chat", "gone-chat", "")], scene.ChatRecords.Select(r => (r.PropertyName, r.OldValue, r.NewValue)));
        }
    }
}
