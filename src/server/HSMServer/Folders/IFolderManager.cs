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


        Task<bool> TryAdd(FolderModel folder);

        List<FolderModel> GetFolders();

        Task Initialize();
    }
}
