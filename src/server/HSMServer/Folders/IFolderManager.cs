using HSMServer.Model.Folders;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Folders
{
    public interface IFolderManager
    {
        FolderModel this[Guid id] { get; }


        Task<bool> TryAdd(FolderModel folder);

        List<FolderModel> GetFolders();

        Task Initialize();
    }
}
