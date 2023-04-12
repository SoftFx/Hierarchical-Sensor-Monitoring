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
        Task<bool> TryAdd(FolderAdd folderAdd, out FolderModel folder);

        Task<bool> TryRemove(Guid folderId);

        void MoveProduct(ProductNodeViewModel product, Guid? fromFolderId, Guid? toFolderId);

        void AddProductToFolder(Guid productId, Guid folderId);

        void UpdateProductInFolder(Guid productId, FolderModel folder);

        void RemoveProductFromFolder(Guid productId);

        bool TryGetValueById(Guid? id, out FolderModel model);

        List<FolderModel> GetUserFolders(User user);
    }
}
