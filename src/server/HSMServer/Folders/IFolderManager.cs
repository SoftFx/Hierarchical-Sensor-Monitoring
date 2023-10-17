using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Folders
{
    public interface IFolderManager : IConcurrentStorageNames<FolderModel, FolderEntity, FolderUpdate>
    {
        event Func<Guid, InitiatorInfo, List<Guid>, Task> RemoveFolderFromChats;

        event Action<Guid, List<Guid>> AddFolderToChats;


        Task<bool> TryAdd(FolderAdd folderAdd, out FolderModel folder);

        Task MoveProduct(ProductNodeViewModel product, Guid? fromFolderId, Guid? toFolderId, InitiatorInfo initiator);

        Task AddProductToFolder(Guid productId, Guid folderId, InitiatorInfo initiator);

        Task RemoveProductFromFolder(Guid productId, Guid folderId, InitiatorInfo initiator);

        Task<string> AddChatToFolder(Guid chatId, Guid folderId, string userName);

        void RemoveChatHandler(TelegramChat chat, InitiatorInfo initiator);

        List<FolderModel> GetUserFolders(User user);
    }
}
