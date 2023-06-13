using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
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


        protected override Action<FolderEntity> AddToDb => _databaseCore.AddFolder;

        protected override Action<FolderEntity> UpdateInDb => _databaseCore.UpdateFolder;

        protected override Action<FolderModel> RemoveFromDb => folder => _databaseCore.RemoveFolder(folder.Id.ToString());

        protected override Func<List<FolderEntity>> GetFromDb => _databaseCore.GetAllFolders;


        public FolderManager(IDatabaseCore databaseCore, ITreeValuesCache cache, IUserManager userManager)
        {
            _databaseCore = databaseCore;

            _cache = cache;
            _cache.ChangeProductEvent += ChangeProductHandler;

            _userManager = userManager;
            _userManager.Removed += RemoveUserHandler;
            _userManager.Added += AddUserHandler;
        }


        public void Dispose()
        {
            _cache.ChangeProductEvent -= ChangeProductHandler;
            _userManager.Removed -= RemoveUserHandler;
        }

        public Task<bool> TryAdd(FolderAdd folderAdd, out FolderModel folder)
        {
            folder = new FolderModel(folderAdd);

            return TryAdd(folder);
        }

        public async override Task<bool> TryAdd(FolderModel model)
        {
            var result = await base.TryAdd(model);

            if (result)
                foreach (var productId in model.Products.Keys)
                    await AddProductToFolder(productId, model.Id);

            return result;
        }

        public async override Task<bool> TryUpdate(FolderUpdate update)
        {
            var result = TryGetValue(update.Id, out var folder) && await base.TryUpdate(update);

            if (result && (update.ExpectedUpdateInterval != null || update.RestoreInterval != null ||
                           update.SavedHistoryPeriod != null || update.SelfDestroy != null))
                foreach (var productId in folder.Products.Keys)
                    TryUpdateProductInFolder(productId, folder);

            return result;
        }

        public async override Task<bool> TryRemove(Guid folderId)
        {
            var result = TryGetValue(folderId, out var folder);

            if (result)
            {
                foreach (var productId in folder.Products.Keys)
                    await RemoveProductFromFolder(productId, folderId);

                foreach (var user in folder.UserRoles.Keys)
                {
                    user.FoldersRoles.Remove(folderId);

                    await _userManager.UpdateUser(user);
                }

                result &= await base.TryRemove(folderId);
            }

            return result;
        }

        public override async Task Initialize()
        {
            await base.Initialize();

            foreach (var (_, folder) in this)
                if (_userManager.TryGetValue(folder.AuthorId, out var author))
                    folder.Author = author.Name;

            foreach (var user in _userManager.GetUsers())
            {
                AddUserHandler(user);
                
                foreach (var (folderId, role) in user.FoldersRoles)
                    if (TryGetValue(folderId, out var folder))
                        folder.UserRoles.Add(user, role);
            }

            ResetServerPolicyForFolderProducts();
        }

        public List<FolderModel> GetUserFolders(User user)
        {
            var folders = GetFolders();

            if (user == null || user.IsAdmin)
                return folders;

            if (user.FoldersRoles.Count == 0)
                return new();

            return folders.Where(f => user.IsFolderAvailable(f.Id)).ToList();
        }

        public async Task MoveProduct(ProductNodeViewModel product, Guid? fromFolderId, Guid? toFolderId)
        {
            if (TryGetValueById(fromFolderId, out var fromFolder))
            {
                fromFolder.Products.Remove(product.Id);
                await RemoveProductFromFolder(product.Id, fromFolderId.Value);
            }

            if (TryGetValueById(toFolderId, out var toFolder))
            {
                toFolder.Products.Add(product.Id, product);
                await AddProductToFolder(product.Id, toFolderId.Value);
            }
        }

        public async Task AddProductToFolder(Guid productId, Guid folderId)
        {
            if (TryGetValue(folderId, out var folder) && TryUpdateProductInFolder(productId, folder))
            {
                foreach (var (user, role) in folder.UserRoles)
                    if (!user.IsUserProduct(productId))
                    {
                        user.ProductsRoles.Add((productId, role));
                        await _userManager.UpdateUser(user);
                    }
            }
        }

        public async Task RemoveProductFromFolder(Guid productId, Guid folderId)
        {
            if (TryGetValue(folderId, out var folder) && TryUpdateProductInFolder(productId, null))
            {
                foreach (var (user, role) in folder.UserRoles)
                    if (user.ProductsRoles.Remove((productId, role)))
                        await _userManager.UpdateUser(user);
            }
        }

        private bool TryUpdateProductInFolder(Guid productId, FolderModel folder)
        {
            var product = _cache.GetProduct(productId);

            if (product is not null)
            {
                var expectedUpdateInterval = product.ServerPolicy.ExpectedUpdate.Policy.Interval;
                var restoreInterval = product.ServerPolicy.RestoreError.Policy.Interval;
                var savedHistory = product.ServerPolicy.SavedHistoryPeriod.Policy.Interval;
                var selfDestroy = product.ServerPolicy.SelfDestroy.Policy.Interval;

                var update = new ProductUpdate()
                {
                    Id = productId,
                    FolderId = folder?.Id ?? Guid.Empty,
                    ExpectedUpdateInterval = GetCorePolicy(expectedUpdateInterval, folder?.ExpectedUpdateInterval),
                    RestoreInterval = GetCorePolicy(restoreInterval, folder?.SensorRestorePolicy),
                    SavedHistoryPeriod = GetCorePolicy(savedHistory, folder?.SavedHistoryPeriod),
                    SelfDestroy = GetCorePolicy(selfDestroy, folder?.SelfDestroyPeriod),
                };

                _cache.UpdateProduct(update);

                return true;
            }

            return false;
        }


        protected override FolderModel FromEntity(FolderEntity entity) => new(entity);


        private void ChangeProductHandler(ProductModel product, ActionType actionType)
        {
            if (actionType == ActionType.Delete && TryGetValueById(product.FolderId, out var folder))
                folder.Products.Remove(product.Id);
        }

        private void AddUserHandler(User user) => user.Tree.GetFolders += GetFolders;

        private void RemoveUserHandler(User user)
        {
            foreach (var folderId in user.FoldersRoles.Keys)
                if (TryGetValue(folderId, out var folder))
                    folder.UserRoles.Remove(user);

            user.Tree.GetFolders -= GetFolders;
        }


        private static TimeIntervalModel GetCorePolicy(TimeIntervalModel coreInterval, TimeIntervalViewModel folderInterval)
        {
            return coreInterval.IsFromFolder
                ? folderInterval?.ToFolderModel() ?? new TimeIntervalModel(coreInterval.CustomPeriod)
                : null;
        }

        private void ResetServerPolicyForFolderProducts()
        {
            static TimeIntervalModel IsFromFolder<T>(CollectionProperty<T> property, TimeIntervalViewModel interval)
                where T : ServerPolicy, new()
            {
                return property.Policy.FromParent ? interval.ToFolderModel() : null;
            }


            foreach (var product in _cache.GetProducts())
            {
                if (!product.FolderId.HasValue || !TryGetValueById(product.FolderId, out var folder))
                    return;

                var update = new ProductUpdate
                {
                    Id = product.Id,
                    ExpectedUpdateInterval = IsFromFolder(product.ServerPolicy.ExpectedUpdate, folder.ExpectedUpdateInterval),
                    RestoreInterval = IsFromFolder(product.ServerPolicy.RestoreError, folder.SensorRestorePolicy),
                    SavedHistoryPeriod = IsFromFolder(product.ServerPolicy.SavedHistoryPeriod, folder.SavedHistoryPeriod),
                    SelfDestroy = IsFromFolder(product.ServerPolicy.SelfDestroy, folder.SelfDestroyPeriod),
                };

                if (update.ExpectedUpdateInterval != null || update.RestoreInterval != null ||
                    update.SavedHistoryPeriod != null || update.SelfDestroy != null)
                    _cache.UpdateProduct(update);
            }
        }

        private List<FolderModel> GetFolders() => Values.Select(x => x.RecalculateState()).ToList();
    }
}