using HSMServer.Model.Folders;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Folders
{
    public interface IFolderManager
    {
        FolderModel this[Guid id] { get; }

        FolderModel this[string name] { get; }


        Task<FolderModel> TryAddFolder(FolderAdd folderAdd);

        Task<bool> TryUpdate(FolderUpdate update);

        Task<bool> TryRemoveFolder(Guid folderId);

        List<FolderModel> GetFolders();

        Task Initialize();
    }
}
