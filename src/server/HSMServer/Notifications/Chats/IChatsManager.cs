using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Folders;
using HSMServer.Notifications.Telegram.Tokens;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Notifications.Chats
{
    public interface IChatsManager : IConcurrentStorage<Chat, ChatEntity, ChatUpdate>
    {
        TokensManager TokenManager { get; }

        event Func<Guid, Guid, string, Task<string>> ConnectChatToFolder;


        string GetChatName(Guid id);

        Chat GetChatByChatId(ChatId chatId);

        string GetInvitationLink(Guid folderId, User user);

        string GetGroupInvitation(Guid folderId, User user);

        // EditChat flow — invite token targets an existing Chat record (binding Telegram in place).
        string GetChatInvitationLink(Guid chatId, User user);

        string GetChatGroupInvitation(Guid chatId, User user);

        Task<ChatConnectResult> TryConnect(Message message, InvitationToken token);

        void AddFolderToChats(Guid folderId, List<Guid> chats);

        Task RemoveFolderFromChats(Guid folderId, List<Guid> chats, InitiatorInfo initiator);

        void RemoveFolderHandler(FolderModel folder, InitiatorInfo initiator);

        Task MigrateToSupergroup(long oldChatId, long newChatId);
    }
}
