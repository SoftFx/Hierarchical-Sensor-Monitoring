using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications.Chats;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi.Chats
{
    // #1825: the chat write engine behind the web flows — seam A of the agreed test plan.
    // The storage managers are mocked: these tests pin the engine's orchestration rules
    // (add-or-update idempotency, partial updates with clear flags, webhook URL validation,
    // the managed-folder binding rule, the initiator on every write). The managers' own
    // semantics live in ChatsManagerTests / ChatFolderBindingTests.
    public sealed class ChatAdministrationServiceTests
    {
        private static readonly Guid AuthorId = Guid.NewGuid();

        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly InitiatorInfo _initiator = InitiatorInfo.AsUser("admin");

        private ChatAdministrationService Engine => new(_chats.Object, _folders.Object);


        [Fact]
        public async Task CreateChat_NewChat_PersistsFieldsAuthorAndBindsTheFolder()
        {
            var chatId = Guid.NewGuid();
            var folderA = new FolderModel(BuildFolderEntity(Guid.NewGuid()));

            SetupChat(chatId, chat: null);
            SetupFolder(folderA);
            _chats.Setup(m => m.TryAdd(It.IsAny<Chat>())).ReturnsAsync(true);
            SetupFolderUpdate(ok: true);

            var update = new ChatUpdate
            {
                Id = chatId,
                Name = "Ops alerts",
                Description = "on-call",
                SendMessages = true,
                MessagesAggregationTimeSec = 30,
                SlackWebhookUrl = "https://hooks.slack.com/services/X",
            };
            var binding = new ChatFolderBinding(Desired: [folderA.Id], Managed: [folderA.Id, Guid.NewGuid()]);

            var result = await Engine.CreateChatAsync(update, binding, AuthorId, _initiator);

            Assert.True(result.Success);
            _chats.Verify(m => m.TryAdd(It.Is<Chat>(c =>
                c.Id == chatId
                && c.AuthorId == AuthorId
                && c.Name == "Ops alerts"
                && c.Description == "on-call"
                && c.SendMessages
                && c.MessagesAggregationTimeSec == 30
                && c.SlackWebhookUrl == "https://hooks.slack.com/services/X")), Times.Once);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u =>
                u.Id == folderA.Id
                && u.Chats.Contains(chatId))), Times.Once);
        }


        [Fact]
        public async Task CreateChat_RowAlreadyExists_UpdatesInsteadOfAdding()
        {
            var chatId = Guid.NewGuid();
            var existing = new Chat(BuildChatEntity(chatId, telegramBound: true));

            SetupChat(chatId, existing);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var update = new ChatUpdate { Id = chatId, Name = "Renamed" };

            var result = await Engine.CreateChatAsync(update, new ChatFolderBinding([], []), AuthorId, _initiator);

            Assert.True(result.Success);
            _chats.Verify(m => m.TryAdd(It.IsAny<Chat>()), Times.Never);
            _chats.Verify(m => m.TryUpdate(It.Is<ChatUpdate>(u => u.Id == chatId && u.Name == "Renamed")), Times.Once);
        }


        [Fact]
        public async Task UpdateChat_PassesThePartialUpdateThrough_StampedWithTheInitiator()
        {
            var chatId = Guid.NewGuid();
            var existing = new Chat(BuildChatEntity(chatId));

            SetupChat(chatId, existing);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var update = new ChatUpdate { Id = chatId, Name = "Renamed", ClearSlackWebhook = true };

            var result = await Engine.UpdateChatAsync(update, new ChatFolderBinding([], []), _initiator);

            Assert.True(result.Success);
            _chats.Verify(m => m.TryUpdate(It.Is<ChatUpdate>(u =>
                u.Id == chatId
                && u.Name == "Renamed"
                && u.ClearSlackWebhook == true
                && u.Initiator == _initiator)), Times.Once);
        }

        [Fact]
        public async Task UpdateChat_MalformedWebhookUrl_IsRejectedBeforeAnyStorageWrite()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId)));

            var update = new ChatUpdate { Id = chatId, SlackWebhookUrl = "ftp://not-a-webhook" };

            var result = await Engine.UpdateChatAsync(update, new ChatFolderBinding([], []), _initiator);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Invalid, result.Failure.Outcome);
            Assert.Contains(nameof(ChatUpdate.SlackWebhookUrl), result.Failure.Errors.Keys);
            _chats.Verify(m => m.TryUpdate(It.IsAny<ChatUpdate>()), Times.Never);
            _folders.Verify(m => m.TryUpdate(It.IsAny<FolderUpdate>()), Times.Never);
        }

        [Fact]
        public async Task UpdateChat_UnknownChat_AnswersNotFound()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, chat: null);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId }, new ChatFolderBinding([], []), _initiator);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
        }

        [Fact]
        public async Task ClearChannel_MapsToTheSingleFlag_NothingElseChanges()
        {
            var chatId = Guid.NewGuid();
            SetupChat(chatId, new Chat(BuildChatEntity(chatId)));
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);

            var result = await Engine.ClearChannelAsync(chatId, ChatChannel.Telegram, _initiator);

            Assert.True(result.Success);
            _chats.Verify(m => m.TryUpdate(It.Is<ChatUpdate>(u =>
                u.Id == chatId
                && u.ClearTelegramBinding == true
                && u.ClearSlackWebhook == null
                && u.ClearMattermostWebhook == null
                && u.Initiator == _initiator)), Times.Once);
        }


        [Fact]
        public async Task UpdateChat_FolderBinding_RemovesOnlyManagedFolders_UnmanagedKeepsItsBinding()
        {
            var chatId = Guid.NewGuid();
            var folderA = new FolderModel(BuildFolderEntity(Guid.NewGuid(), chatId)); // managed
            var folderB = new FolderModel(BuildFolderEntity(Guid.NewGuid(), chatId)); // NOT managed

            var existing = new Chat(BuildChatEntity(chatId));
            existing.Folders.Add(folderA.Id);
            existing.Folders.Add(folderB.Id);

            SetupChat(chatId, existing);
            SetupFolder(folderA);
            SetupFolder(folderB);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);
            SetupFolderUpdate(ok: true);

            // The caller manages only folderA and wants the chat in no folder: folderA is
            // unbound, folderB (outside the managed set) keeps its binding untouched.
            var binding = new ChatFolderBinding(Desired: [], Managed: [folderA.Id]);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId }, binding, _initiator);

            Assert.True(result.Success);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u =>
                u.Id == folderA.Id
                && !u.Chats.Contains(chatId))), Times.Once);
            _folders.Verify(m => m.TryUpdate(It.Is<FolderUpdate>(u => u.Id == folderB.Id)), Times.Never);
        }

        [Fact]
        public async Task UpdateChat_FolderBinding_NeverAddsAFolderOutsideTheManagedSet()
        {
            var chatId = Guid.NewGuid();
            var folderC = new FolderModel(BuildFolderEntity(Guid.NewGuid()));

            var existing = new Chat(BuildChatEntity(chatId));
            SetupChat(chatId, existing);
            SetupFolder(folderC);
            _chats.Setup(m => m.TryUpdate(It.IsAny<ChatUpdate>())).ReturnsAsync(true);
            SetupFolderUpdate(ok: true);

            // The caller asks for folderC but manages no folder at all: nothing is bound.
            var binding = new ChatFolderBinding(Desired: [folderC.Id], Managed: []);

            var result = await Engine.UpdateChatAsync(new ChatUpdate { Id = chatId }, binding, _initiator);

            Assert.True(result.Success);
            _folders.Verify(m => m.TryUpdate(It.IsAny<FolderUpdate>()), Times.Never);
        }

        [Fact]
        public async Task CreateChat_FailedWrite_AnswersConflictAndBindsNoFolders()
        {
            var chatId = Guid.NewGuid();
            var folderA = new FolderModel(BuildFolderEntity(Guid.NewGuid()));

            SetupChat(chatId, chat: null);
            SetupFolder(folderA);
            _chats.Setup(m => m.TryAdd(It.IsAny<Chat>())).ReturnsAsync(false);

            var binding = new ChatFolderBinding(Desired: [folderA.Id], Managed: [folderA.Id]);

            var result = await Engine.CreateChatAsync(new ChatUpdate { Id = chatId, Name = "Ops" }, binding, AuthorId, _initiator);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.Conflict, result.Failure.Outcome);
            _folders.Verify(m => m.TryUpdate(It.IsAny<FolderUpdate>()), Times.Never);
        }


        private static ChatEntity BuildChatEntity(Guid id, bool telegramBound = false) =>
            new()
            {
                Id = id.ToByteArray(),
                Author = AuthorId.ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "pre-created",
                SendMessages = Chat.DefaultSendMessages,
                MessagesAggregationTimeSec = Chat.DefaultMessagesAggregationTimeSec,
                TelegramChatId = telegramBound ? 12345L : null,
            };


        // Moq needs a delegate type for a callback with an out parameter.
        private delegate void OutChat(Guid id, out Chat chat);

        private delegate void OutFolder(Guid id, out FolderModel folder);


        private void SetupChat(Guid chatId, Chat chat) =>
            _chats.Setup(m => m.TryGetValue(chatId, out It.Ref<Chat>.IsAny))
                .Callback(new OutChat((Guid _, out Chat found) => found = chat))
                .Returns(chat is not null);

        private void SetupFolder(FolderModel folder) =>
            _folders.Setup(m => m.TryGetValue(folder.Id, out It.Ref<FolderModel>.IsAny))
                .Callback(new OutFolder((Guid _, out FolderModel found) => found = folder))
                .Returns(true);

        private void SetupFolderUpdate(bool ok) =>
            _folders.Setup(m => m.TryUpdate(It.IsAny<FolderUpdate>())).ReturnsAsync(ok);

        private static FolderEntity BuildFolderEntity(Guid id, params Guid[] chatIds) =>
            new()
            {
                Id = id.ToString(),
                DisplayName = "folder-" + id,
                AuthorId = Guid.NewGuid().ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
                Chats = chatIds.Select(c => c.ToByteArray()).ToList(),
            };
    }
}
