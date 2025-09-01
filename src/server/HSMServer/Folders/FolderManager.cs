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
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSMServer.Folders
{
    public sealed class FolderManager : ConcurrentStorageNames<FolderModel, FolderEntity, FolderUpdate>, IFolderManager
    {
        private readonly ITreeValuesCache _cache;
        private readonly IUserManager _userManager;
        private readonly IDatabaseCore _databaseCore;
        private readonly IJournalService _journalService;
        private readonly ILogger<FolderManager> _logger;

        protected override Action<FolderEntity> AddToDb => _databaseCore.AddFolder;

        protected override Action<FolderEntity> UpdateInDb => _databaseCore.UpdateFolder;

        protected override Action<FolderModel> RemoveFromDb => folder => _databaseCore.RemoveFolder(folder.Id.ToString());

        protected override Func<List<FolderEntity>> GetFromDb => _databaseCore.GetAllFolders;


        public event Func<Guid, List<Guid>, InitiatorInfo, Task> RemoveFolderFromChats;

        public event Action<Guid, List<Guid>> AddFolderToChats;

        public event Func<Guid, string> GetChatName;


        public FolderManager(IDatabaseCore databaseCore, ITreeValuesCache cache, IUserManager userManager, IJournalService journalService, ILogger<FolderManager> logger)
        {
            _databaseCore = databaseCore;
            _logger = logger;

            _cache = cache;
            _cache.ChangeProductEvent += ChangeProductHandler;
            _cache.FillFolderChats += FillFolderChats;

            _userManager = userManager;
            _journalService = journalService;
            _userManager.Removed += RemoveUserHandler;
            _userManager.Added += AddUserHandler;
        }

        private void FillFolderChats(FolderEventArgs e)
        {
            try 
            {
                e.ChatIDs.AddRange(FillFolderChats(e.FolderId));
            }
            catch(Exception ex) 
            {
                e.Error = ex.Message;
                _logger.LogError($"FillFolderChats error: {ex}");
            }
        }

        private List<Guid> FillFolderChats(Guid folderId)
        {
            if (!TryGetValue(folderId, out FolderModel folder))
            {
                _logger.LogError($"FillFolderChats: Folder '{folderId}' not found");
                throw new ApplicationException($"Folder '{folderId}' not found");
            }

            return folder.DefaultChats.SelectedChats.ToList();
        }

        public override void Dispose()
        {
            _cache.FillFolderChats -= FillFolderChats;
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

                if (update.DefaultChats != null || update.TTL != null || update.KeepHistory != null || update.SelfDestroy != null)
                    foreach (var productId in folder.Products.Keys)
                        await TryUpdateProductInFolder(productId, folder, update.Initiator);
            }

            string logFolder = folder?.Name ?? update.Id.ToString();

            if (result)
            {
                StringBuilder sb = new StringBuilder($"Folder '{logFolder}':");
                if (addedTelegramChats.Any())
                    sb.Append($" {addedTelegramChats.Count} chat(s) added");

                if (removedTelegramChats.Any())
                    sb.Append($" {removedTelegramChats.Count} chat(s) removed");

                _logger.LogInformation(sb.ToString());
            }
            else
                _logger.LogWarning($"Folder '{logFolder}' update is unsuccess");

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

            string logFolder = folder?.Name ?? remove.Id.ToString();

            if (result)
                _logger.LogInformation($"Folder '{logFolder}' is removed");
            else
                _logger.LogWarning($"Folder '{logFolder}' remove is unsuccess");

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

            await ResetServerPolicyForFolderProducts();
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

            _logger.LogInformation($"Chat '{chatId}' is added to '{folder?.Name ?? folderId.ToString()}' by user '{userName}'");

            return folder?.Name;
        }

        public void RemoveChatHandler(TelegramChat chat, InitiatorInfo initiator)
        {
            foreach (var (folderId, folder) in this)
            {
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

            _logger.LogInformation($"Chat '{chat.Name}' is removed from all folders by '{initiator}'");
        }

        public List<FolderModel> GetUserFolders(User user)
        {
            var folders = GetFolders();

            if (user == null || user.IsAdmin)
                return folders;

            if (user.FoldersRoles.Count == 0)
                return [];

            return folders.Where(f => user.IsFolderAvailable(f.Id)).ToList();
        }

        public async Task MoveProduct(ProductNodeViewModel product, Guid? fromFolderId, Guid? toFolderId, InitiatorInfo initiator)
        {
            if (TryGetValueById(fromFolderId, out var fromFolder))
            {
                fromFolder.Products.Remove(product.Id);
                await RemoveProductFromFolder(product.Id, fromFolderId.Value, initiator);

                _logger.LogInformation($"MoveProduct: Product '{product.Name}' is removed from folder '{fromFolder.Name}' by '{initiator}'");
            }
            else
                _logger.LogWarning($"MoveProduct: folder from '{fromFolderId}' not found.");

            if (TryGetValueById(toFolderId, out var toFolder))
            {
                toFolder.Products.Add(product.Id, product);
                await AddProductToFolder(product.Id, toFolderId.Value, initiator);

                _logger.LogInformation($"MoveProduct: Product '{product.Name}' is moved to '{toFolder.Name}' by '{initiator}'");
            }
            else
                _logger.LogWarning($"MoveProduct: folder to '{toFolderId}' not found.");
        }

        public async Task AddProductToFolder(Guid productId, Guid folderId, InitiatorInfo initiator)
        {
            if (TryGetValue(folderId, out var folder))
            {
                if (await TryUpdateProductInFolder(productId, folder, initiator))
                {
                    foreach (var (user, role) in folder.UserRoles)
                        if (!user.IsUserProduct(productId))
                        {
                            user.ProductsRoles.Add((productId, role));
                            await _userManager.UpdateUser(user);
                        }

                    _logger.LogInformation($"AddProductToFolder: Product '{productId}' is added to folder '{folder.Name}' by '{initiator}'");
                }
                else
                    _logger.LogWarning($"AddProductToFolder: TryUpdateProductInFolder is unsuccess.");
            }
            else
                _logger.LogWarning($"AddProductToFolder: folder to '{folderId}' not found.");
        }

        public async Task RemoveProductFromFolder(Guid productId, Guid folderId, InitiatorInfo initiator)
        {
            if (TryGetValue(folderId, out var folder))
            {
                if (await TryUpdateProductInFolder(productId, folder, initiator, ActionType.Delete))
                {
                    foreach (var (user, role) in folder.UserRoles)
                        if (user.ProductsRoles.Remove((productId, role)))
                            await _userManager.UpdateUser(user);

                    _logger.LogInformation($"RemoveProductFromFolder: Product '{productId}' is removed from folder '{folder.Name}' by '{initiator}'");
                }
                else
                    _logger.LogWarning($"RemoveProductFromFolder: TryUpdateProductInFolder is unsuccess.");
            }
            else
                _logger.LogWarning($"RemoveProductFromFolder: folder to '{folderId}' not found.");
        }

        public Dictionary<string, string> GetFolderDefaultChats(Guid folderId)
        {
            var chats = new Dictionary<string, string>(1 << 2);

            if (TryGetValue(folderId, out var folder) && folder.DefaultChats.IsCustom)
                foreach (var chat in folder.DefaultChats.SelectedChats)
                    chats.Add(chat.ToString(), GetChatName?.Invoke(chat));

            return chats;
        }

        private async Task<bool> TryUpdateProductInFolder(Guid productId, FolderModel folder, InitiatorInfo initiator, ActionType action = ActionType.Update)
        {
            if (_cache.TryGetProduct(productId, out var product))
            {
                var defaultChats = product.Settings.DefaultChats.Value;

                var savedHistory = product.Settings.KeepHistory.Value;
                var selfDestroy = product.Settings.SelfDestroy.Value;
                var ttl = product.Settings.TTL.Value;

                var update = new ProductUpdate()
                {
                    Id = productId,
                    FolderId = action is ActionType.Delete ? Guid.Empty : folder.Id,

                    DefaultChats = GetCorePolicy(defaultChats, folder, action),
                    KeepHistory = GetCoreUpdate(savedHistory, folder.KeepHistory, action),
                    SelfDestroy = GetCoreUpdate(selfDestroy, folder.SelfDestroy, action),
                    TTL = GetCoreUpdate(ttl, folder.TTL, action),

                    Initiator = initiator,
                };

                await _cache.UpdateProductAsync(update);

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


        private static TimeIntervalModel GetCoreUpdate(TimeIntervalModel model, TimeIntervalViewModel folder, ActionType action)
        {
            var folderModel = folder.ToModel();

            return model.IsFromFolder ? action is ActionType.Delete ? folderModel : folderModel.ToFromFolderModel() : null;
        }

        private PolicyDestinationSettings GetCorePolicy(PolicyDestinationSettings model, FolderModel folder, ActionType action)
        {
            if (model.IsFromFolder || model.IsFromParent)
            {
                var chat = folder.DefaultChats;
                var entity = action is ActionType.Delete
                    ? chat.ToEntity(folder.GetAvailableChats())
                    : Model.Controls.DefaultChatViewModel.FromFolderEntity(GetFolderDefaultChats(folder.Id));

                return new(entity);
            }

            return null;
        }

        private async Task ResetServerPolicyForFolderProducts()
        {
            static TimeIntervalModel IsFromFolder<T>(SettingPropertyBase<T> property, TimeIntervalViewModel interval)
                where T : TimeIntervalModel, new()
            {
                return property.Value.IsFromParent ? interval.ToModel() : null;
            }

            foreach (var product in _cache.GetProducts())
                if (product.FolderId.HasValue && TryGetValueById(product.FolderId, out var folder))
                {
                    var update = new ProductUpdate
                    {
                        Id = product.Id,
                        TTL = IsFromFolder(product.Settings.TTL, folder.TTL),
                        KeepHistory = IsFromFolder(product.Settings.KeepHistory, folder.KeepHistory),
                        SelfDestroy = IsFromFolder(product.Settings.SelfDestroy, folder.SelfDestroy),
                    };

                    if (update.TTL != null || update.KeepHistory != null || update.SelfDestroy != null)
                        await _cache.UpdateProductAsync(update);
                }
        }

        private List<FolderModel> GetFolders() => Values.Select(x => x.RecalculateState()).ToList();


        private string GetChatNameById(Guid id) => GetChatName?.Invoke(id);
    }
}