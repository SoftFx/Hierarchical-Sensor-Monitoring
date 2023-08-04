using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Folders
{
    public interface IFolderManager : IConcurrentStorage<FolderModel, FolderEntity, FolderUpdate>
    {
        event Action<Guid> ResetProductTelegramInheritance;


        Task<bool> TryAdd(FolderAdd folderAdd, out FolderModel folder);

        Task MoveProduct(ProductNodeViewModel product, Guid? fromFolderId, Guid? toFolderId);

        Task AddProductToFolder(Guid productId, Guid folderId);

        Task RemoveProductFromFolder(Guid productId, Guid folderId);

        List<FolderModel> GetUserFolders(User user);
    }
}
