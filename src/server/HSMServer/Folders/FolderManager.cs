﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

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
                    AddProductToFolder(productId, model.Id);

            return result;
        }

        public async override Task<bool> TryRemove(Guid folderId)
        {
            var result = TryGetValue(folderId, out var folder) && await base.TryRemove(folderId);

            if (result)
            {
                foreach (var productId in folder.Products.Keys)
                    RemoveProductFromFolder(productId);

                foreach (var user in folder.UserRoles.Keys)
                {
                    user.FoldersRoles.Remove(folderId);

                    await _userManager.UpdateUser(user);
                }
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
                foreach (var (folderId, role) in user.FoldersRoles)
                    if (TryGetValue(folderId, out var folder))
                        folder.UserRoles.Add(user, role);
        }

        public void MoveProduct(ProductNodeViewModel product, Guid? fromFolderId, Guid? toFolderId)
        {
            if (TryGetValueById(fromFolderId, out var fromFolder))
                fromFolder.Products.Remove(product.Id);

            if (TryGetValueById(toFolderId, out var toFolder))
            {
                toFolder.Products.Add(product.Id, product);
                AddProductToFolder(product.Id, toFolderId.Value);
            }
            else
                RemoveProductFromFolder(product.Id);
        }

        public void AddProductToFolder(Guid productId, Guid folderId)
        {
            if (TryGetValue(folderId, out var folder))
                UpdateProductInFolder(productId, folder);
        }

        public void RemoveProductFromFolder(Guid productId) =>
            UpdateProductInFolder(productId, null);

        public void UpdateProductInFolder(Guid productId, FolderModel folder)
        {
            var product = _cache.GetProduct(productId);

            if (product is not null)
            {
                var expectedUpdateInterval = product.ServerPolicy.ExpectedUpdate.Policy.Interval;
                var restoreInterval = product.ServerPolicy.RestoreError.Policy.Interval;

                var update = new ProductUpdate()
                {
                    Id = productId,
                    FolderId = folder?.Id ?? Guid.Empty,
                    ExpectedUpdateInterval = GetCorePolicy(expectedUpdateInterval, folder?.ExpectedUpdateInterval),
                    RestoreInterval = GetCorePolicy(restoreInterval, folder?.SensorRestorePolicy),
                };

                _cache.UpdateProduct(update);
            }
        }


        public List<FolderModel> GetUserFolders(User user)
        {
            var folders = Values.Select(f => f.RecalculateState()).ToList();

            if (user == null || user.IsAdmin)
                return folders;

            if (user.FoldersRoles.Count == 0)
                return new();

            return folders.Where(f => user.IsFolderAvailable(f.Id)).ToList();
        }


        protected override FolderModel FromEntity(FolderEntity entity) => new(entity);


        private void ChangeProductHandler(ProductModel product, ActionType actionType)
        {
            if (actionType == ActionType.Delete && TryGetValueById(product.FolderId, out var folder))
                folder.Products.Remove(product.Id);
        }

        private void RemoveUserHandler(User user)
        {
            foreach (var folderId in user.FoldersRoles.Keys)
                if (TryGetValue(folderId, out var folder))
                    folder.UserRoles.Remove(user);
        }

        private static TimeIntervalModel GetCorePolicy(TimeIntervalModel coreInterval, TimeIntervalViewModel folderInterval)
        {
            return coreInterval.IsFromFolder
                ? folderInterval?.ToFolderModel() ?? new TimeIntervalModel(coreInterval.CustomPeriod)
                : null;
        }
    }
}
