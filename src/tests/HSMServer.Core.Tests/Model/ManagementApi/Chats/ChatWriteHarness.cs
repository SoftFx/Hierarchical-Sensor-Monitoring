using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Notifications.Chats;
using HSMCommon.Model;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace HSMServer.Core.Tests.Model.ManagementApi.Chats
{
    // The in-memory world of the chat write surface (#1829), shared by the service's and
    // the REST controller's tests: a chat store and a folder store wired to each other the
    // way NotificationsCenter wires the real managers, the evaluator's decisions, the
    // token's owner and the usage counter. Every echo reads back what the engine stored.
    internal sealed class ChatWriteHarness
    {
        public const string SlackUrl = "https://hooks.slack.com/services/T000/B000/XXXXXXXX";
        public const string MattermostUrl = "https://mattermost.example.com/hooks/yyyyyyyyyyyyyyy";
        public const string FolderNotManagedMessage = "Every folder must exist and be one the token's owner manages.";
        public const string MakingGlobalDeniedMessage = "Only an administrator can make a chat global: leave it bound to at least one folder.";

        public static readonly Guid OwnerId = Guid.NewGuid();

        private static readonly string TokenId = new('A', ApiTokenMaterial.TokenIdLength);

        public Mock<IChatsManager> Chats { get; } = new();
        public Mock<IFolderManager> Folders { get; } = new();
        public Mock<IApiTokenAuthorizationService> Authorization { get; } = new();
        public Mock<IUserManager> Users { get; } = new();
        public Mock<IChatSensorUsageCalculator> Usage { get; } = new();

        public List<Chat> Store { get; } = [];
        public List<FolderModel> FolderStore { get; } = [];

        // The folders the token's owner does NOT manage: a folder is managed unless a test
        // narrows it here.
        public HashSet<Guid> Unmanaged { get; } = [];

        public User Owner { get; } = new("admin") { Id = OwnerId, IsAdmin = true };

        public ClaimsPrincipal Token { get; }


        public ChatWriteHarness()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, OwnerId.ToString()),
                new Claim(HsmApiTokenClaims.TokenId, TokenId),
            ], HsmApiTokenDefaults.AuthenticationScheme);

            Token = new ClaimsPrincipal(identity);

            Users.Setup(u => u[OwnerId]).Returns(Owner);

            Chats.Setup(c => c.GetValues()).Returns(() => Store.ToList());
            Chats.Setup(c => c[It.IsAny<Guid>()]).Returns((Guid id) => Store.FirstOrDefault(chat => chat.Id == id));
            Chats.Setup(c => c.TryGetValue(It.IsAny<Guid>(), out It.Ref<Chat>.IsAny))
                .Returns(new TryGetChat((Guid id, out Chat chat) =>
                {
                    chat = Store.FirstOrDefault(c => c.Id == id);
                    return chat is not null;
                }));
            Chats.Setup(c => c.TryAdd(It.IsAny<Chat>())).Callback<Chat>(Store.Add).ReturnsAsync(true);
            Chats.Setup(c => c.TryUpdate(It.IsAny<ChatUpdate>()))
                .Callback<ChatUpdate>(update => Store.Single(c => c.Id == update.Id).Update(update))
                .ReturnsAsync(true);
            // The removal reaches the folders the way NotificationsCenter wires it in
            // production: ChatsManager's Removed event runs FolderManager.RemoveChatHandler,
            // which prunes the chat id from every folder bound to it.
            Chats.Setup(c => c.TryRemove(It.IsAny<RemoveRequest>()))
                .Callback<RemoveRequest>(remove =>
                {
                    Store.RemoveAll(c => c.Id == remove.Id);

                    foreach (var folder in FolderStore)
                        folder.Chats.Remove(remove.Id);
                })
                .ReturnsAsync(true);

            Folders.Setup(f => f.GetValues()).Returns(() => FolderStore.ToList());
            Folders.Setup(f => f.TryGetValue(It.IsAny<Guid>(), out It.Ref<FolderModel>.IsAny))
                .Returns(new TryGetFolder((Guid id, out FolderModel folder) =>
                {
                    folder = FolderStore.FirstOrDefault(f => f.Id == id);
                    return folder is not null;
                }));
            // A folder write reaches the chats' reverse index the way NotificationsCenter
            // wires it in production (FolderManager's Added/Removed chat events).
            Folders.Setup(f => f.TryUpdate(It.IsAny<FolderUpdate>())).ReturnsAsync(true).Callback<FolderUpdate>(update =>
            {
                var folder = FolderStore.Single(f => f.Id == update.Id);

                foreach (var chatId in folder.Chats.Except(update.Chats))
                    Store.Single(c => c.Id == chatId).Folders.Remove(folder.Id);

                folder.Update(update);

                foreach (var chatId in folder.Chats)
                    Store.Single(c => c.Id == chatId).Folders.Add(folder.Id);
            });

            // An admin owner entitled by default; the refusal scenarios override one decision.
            // The chat admission itself (sight and the change gate) is the evaluator's, pinned
            // in ApiTokenAuthorizationServiceTests: here its decision is an input.
            Authorization.Setup(a => a.AuthorizeChatCreate(It.IsAny<ClaimsPrincipal>()))
                .Returns(ApiTokenAuthorization.Allowed);
            Authorization.Setup(a => a.AuthorizeChatWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(ApiTokenAuthorization.Allowed);
            // Every folder is in the caller's sight by default (the echo and the delete
            // preview list only visible bindings).
            Authorization.Setup(a => a.IsVisible(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(true);
            Authorization.Setup(a => a.CanSeeChatDetail(It.IsAny<ClaimsPrincipal>(), It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(true);
            Authorization.Setup(a => a.CanSeeFolderDetail(It.IsAny<ClaimsPrincipal>(), It.IsAny<Guid>()))
                .Returns((ClaimsPrincipal _, Guid folderId) => FolderStore.Any(f => f.Id == folderId) && !Unmanaged.Contains(folderId));
        }

        // The evaluator refuses the change of a chat: the admission decision a non-manager
        // (Forbidden) or a chat outside the owner's sight (NotFound) produces.
        public void RefuseChatWrites(ApiTokenAuthorization decision) =>
            Authorization.Setup(a => a.AuthorizeChatWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>()))
                .Returns(decision);


        public ChatWriteService CreateWriter() => new(Chats.Object, new ChatAdministrationService(Chats.Object, Folders.Object),
            Authorization.Object, Folders.Object, Users.Object, Usage.Object);

        public ChatsReadService CreateReader() => new(Chats.Object, Authorization.Object);

        public FolderModel AddFolder(params Guid[] boundChats)
        {
            var folder = new FolderModel(new FolderEntity
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = "folder-" + FolderStore.Count,
                AuthorId = OwnerId.ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
                Chats = [.. boundChats.Select(c => c.ToByteArray())],
            });

            // FolderManager wires the chat-name resolver of every folder it loads; the
            // folder's binding update journals through it.
            folder.GetChatName += chatId => Store.FirstOrDefault(c => c.Id == chatId)?.Name ?? chatId.ToString();

            FolderStore.Add(folder);

            foreach (var chatId in boundChats)
                Store.Single(c => c.Id == chatId).Folders.Add(folder.Id);

            return folder;
        }

        public Chat AddChat(string name, bool global = true, string description = null)
        {
            var chat = new Chat(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = OwnerId.ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = name,
                Description = description,
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
            });

            Store.Add(chat);

            if (!global)
                AddFolder(chat.Id);

            return chat;
        }


        // Moq needs a delegate type for a return value that also sets an out parameter.
        private delegate bool TryGetChat(Guid id, out Chat chat);

        private delegate bool TryGetFolder(Guid id, out FolderModel folder);
    }
}
