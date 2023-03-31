﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
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

        List<FolderModel> GetFolders();
    }
}
