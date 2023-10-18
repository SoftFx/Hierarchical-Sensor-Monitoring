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

namespace HSMServer.Notifications
{
    public interface ITelegramChatsManager : IConcurrentStorage<TelegramChat, TelegramChatEntity, TelegramChatUpdate>
    {
        TokensManager TokenManager { get; }

        event Func<Guid, Guid, string, Task<string>> ConnectChatToFolder;


        string GetInvitationLink(Guid folderId, User user);

        string GetGroupInvitation(Guid folderId, User user);

        Task<string> TryConnect(Message message, InvitationToken token);

        void AddFolderToChats(Guid folderId, List<Guid> chats);

        Task RemoveFolderFromChats(Guid folderId, List<Guid> chats, InitiatorInfo initiator);

        void RemoveFolderHandler(FolderModel folder, InitiatorInfo initiator);
    }
}
