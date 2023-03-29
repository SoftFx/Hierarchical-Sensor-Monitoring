using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
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
        private readonly ITreeValuesCache _cache;
        private readonly IUserManager _userManager;
        private readonly IDatabaseCore _databaseCore;
        private readonly TreeViewModel _treeViewModel;
        private readonly ILogger<FolderManager> _logger;


        protected override Action<FolderEntity> AddToDb => _databaseCore.AddFolder;

        protected override Action<FolderEntity> UpdateInDb => _databaseCore.UpdateFolder;

        protected override Action<FolderModel> RemoveFromDb => folder => _databaseCore.RemoveFolder(folder.Id.ToString());

        protected override Func<List<FolderEntity>> GetFromDb => _databaseCore.GetAllFolders;


        public FolderManager(IDatabaseCore databaseCore, ITreeValuesCache cache,
            IUserManager userManager, TreeViewModel treeViewModel, ILogger<FolderManager> logger)
        {
            _treeViewModel = treeViewModel;
            _databaseCore = databaseCore;
            _logger = logger;

            _cache = cache;
            _cache.ChangeProductEvent += ChangeProductHandler;

            _userManager = userManager;
            _userManager.Removed += RemoveUserHandler;
        }


        public async Task<FolderModel> TryAddFolder(FolderAdd folderAdd)
        {
            var folder = new FolderModel(folderAdd);
            var result = await TryAdd(folder);

            if (result)
                foreach (var product in folder.Products)
                    _cache.AddProductFolder(product.Id, folder.Id);

            return result ? folder : null;
        }

        public async Task<bool> TryRemoveFolder(Guid folderId)
        {
            var result = TryGetValue(folderId, out var folder) && await TryRemove(folderId);

            if (result)
            {
                foreach (var product in folder.Products)
                    _cache.RemoveProductFolder(product.Id);

                foreach (var (user, _) in folder.UserRoles)
                {
                    user.FoldersRoles.Remove(folderId);

                    await _userManager.UpdateUser(user);
                }
            }

            return result;
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


        private void ChangeProductHandler(ProductModel product, ActionType actionType)
        {
            if (actionType == ActionType.Delete && product.FolderId.HasValue)
                if (TryGetValue(product.FolderId.Value, out var folder))
                    folder.Products.RemoveAll(p => p.Id == product.Id);
        }

        private void RemoveUserHandler(User user)
        {
            foreach (var (folderId, _) in user.FoldersRoles)
                if (TryGetValue(folderId, out var folder))
                    folder.UserRoles.Remove(user);
        }
    }
}
