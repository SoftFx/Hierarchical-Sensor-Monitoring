using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Folders
{
    public interface IFolderManager : IConcurrentStorage<FolderModel, FolderEntity, FolderUpdate>
    {
        Task<FolderModel> TryAddFolder(FolderAdd folderAdd);

        Task<bool> TryRemoveFolder(Guid folderId);

        bool TryGetValueById(Guid? id, out FolderModel model);

        List<FolderModel> GetUserFolders(User user);
    }
}
