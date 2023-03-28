using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Folders
{
    public sealed class FolderManager : ConcurrentStorage<FolderModel, FolderEntity, FolderUpdate>, IFolderManager
    {
        private readonly IUserManager _userManager;
        private readonly IDatabaseCore _databaseCore;
        private readonly TreeViewModel _treeViewModel;
        private readonly ILogger<FolderManager> _logger;


        protected override Action<FolderEntity> AddToDb => _databaseCore.AddFolder;

        protected override Action<FolderEntity> UpdateInDb => _databaseCore.UpdateFolder;

        protected override Action<FolderModel> RemoveFromDb => folder => _databaseCore.RemoveFolder(folder.Id.ToString());

        protected override Func<List<FolderEntity>> GetFromDb => _databaseCore.GetAllFolders;


        public FolderManager(IDatabaseCore databaseCore, IUserManager userManager,
            TreeViewModel treeViewModel, ILogger<FolderManager> logger)
        {
            _databaseCore = databaseCore;
            _userManager = userManager;
            _treeViewModel = treeViewModel;
            _logger = logger;
        }


        public List<FolderModel> GetFolders() => Values.ToList();

        public override async Task Initialize()
        {
            await base.Initialize();

            foreach (var (_, folder) in this)
                if (_userManager.TryGetValue(folder.AuthorId, out var author))
                    folder.Author = author.Name;

            foreach (var (_, node) in _treeViewModel.Nodes)
                if (node.Parent is null && node.FolderId.HasValue && TryGetValue(node.FolderId.Value, out var folder))
                    folder.Products.Add(node);

            foreach (var user in _userManager.GetUsers())
                foreach (var (folderId, role) in user.FoldersRoles)
                    if (TryGetValue(folderId, out var folder))
                        folder.UserRoles.Add(user, role);
        }

        protected override FolderModel FromEntity(FolderEntity entity) => new(entity);
    }
}
