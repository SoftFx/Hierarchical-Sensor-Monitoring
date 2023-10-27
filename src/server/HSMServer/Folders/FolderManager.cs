using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Folders
{
    public sealed class FolderManager : ConcurrentStorageNames<FolderModel, FolderEntity, FolderUpdate>, IFolderManager
    {
        private readonly ITreeValuesCache _cache;
        private readonly IUserManager _userManager;
        private readonly IDatabaseCore _databaseCore;
        private readonly IJournalService _journalService;


        protected override Action<FolderEntity> AddToDb => _databaseCore.AddFolder;

        protected override Action<FolderEntity> UpdateInDb => _databaseCore.UpdateFolder;

        protected override Action<FolderModel> RemoveFromDb => folder => _databaseCore.RemoveFolder(folder.Id.ToString());

        protected override Func<List<FolderEntity>> GetFromDb => _databaseCore.GetAllFolders;


        public event Func<Guid, List<Guid>, InitiatorInfo, Task> RemoveFolderFromChats;

        public event Action<Guid, List<Guid>> AddFolderToChats;

        public event Func<Guid, string> GetChatName;


        public FolderManager(IDatabaseCore databaseCore, ITreeValuesCache cache, IUserManager userManager, IJournalService journalService)
        {
            _databaseCore = databaseCore;

            _cache = cache;
            _cache.ChangeProductEvent += ChangeProductHandler;

            _userManager = userManager;
            _journalService = journalService;
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

            return TryAdd(folder, folderAdd.Initiator);
        }

        public async Task<bool> TryAdd(FolderModel model, InitiatorInfo info)
        {
            var result = await base.TryAdd(model);

            if (result)
            {
                foreach (var productId in model.Products.Keys)
                    await AddProductToFolder(productId, model.Id, info);

                model.GetChatName += GetChatNameById;
                model.ChangesHandler += _journalService.AddRecord;
            }

            return result;
        }

        public override Task<bool> TryAdd(FolderModel model) => TryAdd(model, InitiatorInfo.System); //TODO initiator should be added in ConcurrentStorage


        public async override Task<bool> TryUpdate(FolderUpdate update)
        {
            var result = TryGetValue(update.Id, out var folder);


            var addedTelegramChats = new List<Guid>(1 << 2);
            var removedTelegramChats = new List<Guid>(1 << 2);

            if (update.TelegramChats is not null)
            {
                addedTelegramChats.AddRange(update.TelegramChats.Except(folder.TelegramChats));
                removedTelegramChats.AddRange(folder.TelegramChats.Except(update.TelegramChats));
            }


            result &= await base.TryUpdate(update);

            if (result)
            {
                AddFolderToChats?.Invoke(folder.Id, addedTelegramChats);
                await (RemoveFolderFromChats?.Invoke(folder.Id, removedTelegramChats, update.Initiator) ?? Task.CompletedTask);

                if (update.TTL != null || update.KeepHistory != null || update.SelfDestroy != null)
                    foreach (var productId in folder.Products.Keys)
                        TryUpdateProductInFolder(productId, folder, update.Initiator);
            }

            return result;
        }

        public override async Task<bool> TryRemove(RemoveRequest remove)
        {
            var result = TryGetValue(remove.Id, out var folder);

            if (result)
            {
                foreach (var productId in folder.Products.Keys)
                    await RemoveProductFromFolder(productId, remove.Id, remove.Initiator);

                foreach (var user in folder.UserRoles.Keys)
                {
                    user.FoldersRoles.Remove(remove.Id);

                    await _userManager.UpdateUser(user);
                }

                result &= await base.TryRemove(remove);
            }

            return result;
        }


        public override async Task Initialize()
        {
            await base.Initialize();

            foreach (var (_, folder) in this)
            {
                folder.GetChatName += GetChatNameById;
                folder.ChangesHandler += _journalService.AddRecord;

                if (_userManager.TryGetValue(folder.AuthorId, out var author))
                    folder.Author = author.Name;
            }

            foreach (var user in _userManager.GetUsers())
            {
                AddUserHandler(user);

                foreach (var (folderId, role) in user.FoldersRoles)
                    if (TryGetValue(folderId, out var folder))
                        folder.UserRoles.Add(user, role);
            }

            ResetServerPolicyForFolderProducts();
        }


        public async Task<string> AddChatToFolder(Guid chatId, Guid folderId, string userName)
        {
            if (TryGetValue(folderId, out var folder) && !folder.TelegramChats.Contains(chatId))
            {
                var update = new FolderUpdate()
                {
                    Id = folderId,
                    TelegramChats = new HashSet<Guid>(folder.TelegramChats) { chatId },
                    Initiator = InitiatorInfo.AsUser(userName),
                };

                await TryUpdate(update);
            }

            return folder?.Name;
        }

        public void RemoveChatHandler(TelegramChat chat, InitiatorInfo initiator)
        {
            foreach (var (folderId, folder) in this)
                if (folder.TelegramChats.Contains(chat.Id))
                {
                    var chats = new HashSet<Guid>(folder.TelegramChats);
                    chats.Remove(chat.Id);

                    var update = new FolderUpdate()
                    {
                        Id = folderId,
                        TelegramChats = chats,
                        Initiator = initiator,
                    };

                    _ = TryUpdate(update);
                }
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

        public async Task MoveProduct(ProductNodeViewModel product, Guid? fromFolderId, Guid? toFolderId, InitiatorInfo initiator)
        {
            if (TryGetValueById(fromFolderId, out var fromFolder))
            {
                fromFolder.Products.Remove(product.Id);
                await RemoveProductFromFolder(product.Id, fromFolderId.Value, initiator);
            }

            if (TryGetValueById(toFolderId, out var toFolder))
            {
                toFolder.Products.Add(product.Id, product);
                await AddProductToFolder(product.Id, toFolderId.Value, initiator);
            }
        }

        public async Task AddProductToFolder(Guid productId, Guid folderId, InitiatorInfo initiator)
        {
            if (TryGetValue(folderId, out var folder) && TryUpdateProductInFolder(productId, folder, initiator))
            {
                foreach (var (user, role) in folder.UserRoles)
                    if (!user.IsUserProduct(productId))
                    {
                        user.ProductsRoles.Add((productId, role));
                        await _userManager.UpdateUser(user);
                    }
            }
        }

        public async Task RemoveProductFromFolder(Guid productId, Guid folderId, InitiatorInfo initiator)
        {
            if (TryGetValue(folderId, out var folder) && TryUpdateProductInFolder(productId, folder, initiator, ActionType.Delete))
            {
                foreach (var (user, role) in folder.UserRoles)
                    if (user.ProductsRoles.Remove((productId, role)))
                        await _userManager.UpdateUser(user);
            }
        }

        private bool TryUpdateProductInFolder(Guid productId, FolderModel folder, InitiatorInfo initiator, ActionType action = ActionType.Update)
        {
            var product = _cache.GetProduct(productId);

            if (product is not null)
            {
                var ttl = product.Settings.TTL.Value;
                var savedHistory = product.Settings.KeepHistory.Value;
                var selfDestroy = product.Settings.SelfDestroy.Value;

                var update = new ProductUpdate()
                {
                    Id = productId,
                    FolderId = action is ActionType.Delete ? Guid.Empty : folder.Id,
                    TTL = GetCorePolicy(ttl, folder.TTL, action),
                    KeepHistory = GetCorePolicy(savedHistory, folder.KeepHistory, action),
                    SelfDestroy = GetCorePolicy(selfDestroy, folder.SelfDestroy, action),
                    Initiator = initiator,
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

        private void RemoveUserHandler(User user, InitiatorInfo _)
        {
            foreach (var folderId in user.FoldersRoles.Keys)
                if (TryGetValue(folderId, out var folder))
                    folder.UserRoles.Remove(user);

            user.Tree.GetFolders -= GetFolders;
        }


        private static TimeIntervalModel GetCorePolicy(TimeIntervalModel model, TimeIntervalViewModel folder, ActionType action)
        {
            var folderModel = folder.ToModel();

            return model.IsFromFolder ? action is ActionType.Delete ? folderModel : folderModel.ToFromFolderModel() : null;
        }

        private void ResetServerPolicyForFolderProducts()
        {
            static TimeIntervalModel IsFromFolder<T>(SettingProperty<T> property, TimeIntervalViewModel interval)
                where T : TimeIntervalModel, new()
            {
                return property.Value.IsFromParent ? interval.ToModel() : null;
            }

            foreach (var product in _cache.GetProducts())
            {
                if (!product.FolderId.HasValue || !TryGetValueById(product.FolderId, out var folder))
                    continue;

                var update = new ProductUpdate
                {
                    Id = product.Id,
                    TTL = IsFromFolder(product.Settings.TTL, folder.TTL),
                    KeepHistory = IsFromFolder(product.Settings.KeepHistory, folder.KeepHistory),
                    SelfDestroy = IsFromFolder(product.Settings.SelfDestroy, folder.SelfDestroy),
                };

                if (update.TTL != null || update.KeepHistory != null || update.SelfDestroy != null)
                    _cache.UpdateProduct(update);
            }
        }

        private List<FolderModel> GetFolders() => Values.Select(x => x.RecalculateState()).ToList();


        private string GetChatNameById(Guid id) => GetChatName?.Invoke(id);
    }
}