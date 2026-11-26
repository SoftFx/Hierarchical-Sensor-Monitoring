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
using HSMServer.Extensions;
using HSMServer.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications.Chats;
using NLog;
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
        private readonly IChatsManager _chatsManager;

        // #1570: folder-bound template entries of the undo journal are
        // purged with the folder (constructed over the same DB core — the
        // journal is a thin facade).
        private readonly HSMServer.Core.ChangeSets.IChangeSetJournal _changeSetJournal;

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        protected override Action<FolderEntity> AddToDb => _databaseCore.AddFolder;

        protected override Action<FolderEntity> UpdateInDb => _databaseCore.UpdateFolder;

        protected override Action<FolderModel> RemoveFromDb => folder => _databaseCore.RemoveFolder(folder.Id.ToString());

        protected override Func<List<FolderEntity>> GetFromDb => _databaseCore.GetAllFolders;


        public event Func<Guid, List<Guid>, InitiatorInfo, Task> RemoveFolderFromChats;

        public event Action<Guid, List<Guid>> AddFolderToChats;

        public event Func<Guid, string> GetChatName;


        public FolderManager(IDatabaseCore databaseCore, ITreeValuesCache cache, IUserManager userManager, IJournalService journalService,
                             IChatsManager chatsManager,
                             HSMServer.Core.ChangeSets.IChangeSetJournal changeSetJournal = null)
        {
            _databaseCore = databaseCore;

            _cache = cache;
            _cache.ChangeProductEvent += ChangeProductHandler;
            _cache.FillFolderChats += FillFolderChats;

            _userManager = userManager;
            _journalService = journalService;
            _userManager.Removed += RemoveUserHandler;
            _userManager.Added += AddUserHandler;

            _chatsManager = chatsManager;

            // The SINGLETON journal when DI provides it (the process-wide
            // read-modify-write lock must be shared with the write hooks);
            // the local fallback serves only the mocked harnesses.
            _changeSetJournal = changeSetJournal ?? new HSMServer.Core.ChangeSets.ChangeSetJournal(databaseCore);
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
                _logger.Error($"FillFolderChats error: {ex}");
            }
        }

        private List<Guid> FillFolderChats(Guid folderId)
        {
            if (!TryGetValue(folderId, out FolderModel folder))
            {
                _logger.Error($"FillFolderChats: Folder '{folderId}' not found");
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
                model.GetGlobalChatIds += GetGlobalChatIds;
                model.ChangesHandler += _journalService.AddRecord;

                // The tree-CRUD trace of the folder's creation (#1588):
                // journal-visible only, no undo group.
                _journalService.AddRecord(new JournalRecordModel(model.Id, info)
                {
                    Enviroment = "Create folder",
                    PropertyName = "folder",
                    NewValue = model.Name,
                    Kind = HSMServer.Core.Journal.JournalKinds.TreeCrud,
                });
            }

            return result;
        }

        public override Task<bool> TryAdd(FolderModel model) => TryAdd(model, InitiatorInfo.System); //TODO initiator should be added in ConcurrentStorage


        public async override Task<bool> TryUpdate(FolderUpdate update)
        {
            var result = TryGetValue(update.Id, out var folder);


            var addedChats = new List<Guid>(1 << 2);
            var removedChats = new List<Guid>(1 << 2);

            if (update.Chats is not null)
            {
                addedChats.AddRange(update.Chats.Except(folder.Chats));
                removedChats.AddRange(folder.Chats.Except(update.Chats));
            }


            result &= await base.TryUpdate(update);

            if (result)
            {
                AddFolderToChats?.Invoke(folder.Id, addedChats);
                await FanOutRemoveFolderFromChats(folder.Id, removedChats, update.Initiator);

                if (removedChats.Count > 0)
                {
                    var chatsToPrune = removedChats.Where(id => !IsChatGlobal(id)).ToList();
                    if (chatsToPrune.Count > 0)
                        await _cache.RemoveChatsFromPoliciesAsync(folder.Id, chatsToPrune, update.Initiator);
                }

                if (update.DefaultChats != null || update.TTL != null || update.KeepHistory != null || update.SelfDestroy != null)
                    foreach (var productId in folder.Products.Keys)
                        await TryUpdateProductInFolder(productId, folder, update.Initiator);
            }

            string logFolder = folder?.Name ?? update.Id.ToString();

            if (result)
            {
                StringBuilder sb = new StringBuilder($"Folder '{logFolder}':");
                if (addedChats.Any())
                    sb.Append($" {addedChats.Count} chat(s) added");

                if (removedChats.Any())
                    sb.Append($" {removedChats.Count} chat(s) removed");

                _logger.Info(sb.ToString());
            }
            else
                _logger.Warn($"Folder '{logFolder}' update is unsuccess");

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

                // #1570: the folder's undo-journal records (folder-bound
                // template entries) do not outlive the folder; surviving
                // change sets go partial. Guid.Empty matches no entity id —
                // the purge runs on the FOLDER arm only. A purge failure is
                // logged and swallowed — the folder is already gone and the
                // deletion must not fail over journal cleanup (the
                // TreeValuesCache.PurgeChangeSets rule).
                if (result)
                {
                    try
                    {
                        _changeSetJournal.PurgeReferences(Guid.Empty, remove.Id);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, $"Change-set purge failed for folder {remove.Id}");
                    }
                }
            }

            string logFolder = folder?.Name ?? remove.Id.ToString();

            if (result)
            {
                _logger.Info($"Folder '{logFolder}' is removed");

                // The tree-CRUD trace of the folder's deletion (#1588):
                // journal-visible only, no undo group.
                _journalService.AddRecord(new JournalRecordModel(remove.Id, remove.Initiator)
                {
                    Enviroment = "Remove folder",
                    PropertyName = "folder",
                    OldValue = logFolder,
                    Kind = HSMServer.Core.Journal.JournalKinds.TreeCrud,
                });
            }
            else
                _logger.Warn($"Folder '{logFolder}' remove is unsuccess");

            return result;
        }


        public override async Task Initialize()
        {
            await base.Initialize();

            foreach (var (_, folder) in this)
            {
                folder.GetChatName += GetChatNameById;
                folder.GetGlobalChatIds += GetGlobalChatIds;
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
            await ResolveInheritedCalendarPresets();
        }


        public async Task<string> AddChatToFolder(Guid chatId, Guid folderId, string userName)
        {
            if (TryGetValue(folderId, out var folder) && !folder.Chats.Contains(chatId))
            {
                var update = new FolderUpdate()
                {
                    Id = folderId,
                    Chats = new HashSet<Guid>(folder.Chats) { chatId },
                    Initiator = InitiatorInfo.AsUser(userName),
                };

                await TryUpdate(update);
            }

            _logger.Info($"Chat '{chatId}' is added to '{folder?.Name ?? folderId.ToString()}' by user '{userName}'");

            return folder?.Name;
        }

        public void RemoveChatHandler(Chat chat, InitiatorInfo initiator)
        {
            foreach (var (folderId, folder) in this)
            {
                if (folder.Chats.Contains(chat.Id))
                {
                    var chats = new HashSet<Guid>(folder.Chats);
                    chats.Remove(chat.Id);

                    var update = new FolderUpdate()
                    {
                        Id = folderId,
                        Chats = chats,
                        Initiator = initiator,
                    };

                    _ = TryUpdate(update);
                }
            }

            _logger.Info($"Chat '{chat.Name}' is removed from all folders by '{initiator}'");
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
            var fromFound = TryGetValueById(fromFolderId, out var fromFolder);
            var toFound = TryGetValueById(toFolderId, out var toFolder);

            // The folder the product is in is not a destination: taking the
            // product out and putting it back would turn the settings it
            // inherits from the folder into its own and re-create its template
            // alerts (new policies, the per-sensor enable/disable choices gone).
            if (fromFound && toFound && fromFolder.Id == toFolder.Id)
            {
                _logger.Info($"MoveProduct: Product '{product.Name}' is already in folder '{toFolder.Name}', nothing to move.");
                return;
            }

            if (fromFound && toFound)
            {
                // The product's folder changes once, from the old one straight to
                // the new one. It used to change twice (out of the old folder,
                // then into the new one), and the cache follows a change of folder
                // at once: in no folder in between, the product lost every
                // template alert to the first update, values already queued were
                // evaluated without them, and a dashboard subscription for
                // products without a folder took the sensors for good.
                //
                // The two steps and their order are kept, so the product ends up
                // with what it always did: first what it inherited from the old
                // folder becomes its own (as when it leaves a folder, but it
                // stays in it), then it enters the new folder.
                fromFolder.Products.Remove(product.Id);

                if (await TryUpdateProductInFolder(product.Id, fromFolder, initiator, ActionType.Delete, keepsFolder: true))
                {
                    await RevokeFolderRoles(fromFolder, product.Id);

                    // Listed in the new folder only now, as it always was: a
                    // settings update of that folder must not reach the product
                    // before it stopped inheriting from the old one.
                    toFolder.Products[product.Id] = product;

                    await TryUpdateProductInFolder(product.Id, toFolder, initiator);
                    await GrantFolderRoles(toFolder, product.Id);

                    _logger.Info($"MoveProduct: Product '{product.Name}' is moved from '{fromFolder.Name}' to '{toFolder.Name}' by '{initiator}'");
                }
                else
                    _logger.Warn($"MoveProduct: product '{product.Id}' was not found.");
            }
            else
            {
                if (fromFound)
                {
                    fromFolder.Products.Remove(product.Id);
                    await RemoveProductFromFolder(product.Id, fromFolderId.Value, initiator);

                    _logger.Info($"MoveProduct: Product '{product.Name}' is removed from folder '{fromFolder.Name}' by '{initiator}'");
                }
                else
                    _logger.Warn($"MoveProduct: folder from '{fromFolderId}' not found.");

                if (toFound)
                {
                    toFolder.Products.Add(product.Id, product);
                    await AddProductToFolder(product.Id, toFolderId.Value, initiator);

                    _logger.Info($"MoveProduct: Product '{product.Name}' is moved to '{toFolder.Name}' by '{initiator}'");
                }
                else
                    _logger.Warn($"MoveProduct: folder to '{toFolderId}' not found.");
            }

            // The tree-CRUD trace of the move (#1588), written AFTER the
            // move and ONLY when a folder was actually resolved — an unknown
            // pair journals two Warn lines and nothing else. Journal-visible
            // only.
            if (fromFolder is not null || toFolder is not null)
                _journalService.AddRecord(new JournalRecordModel(product.Id, initiator)
                {
                    Enviroment = "Move product",
                    PropertyName = "product",
                    OldValue = fromFolder?.Name,
                    NewValue = toFolder?.Name,
                    Path = product.Name,
                    Kind = HSMServer.Core.Journal.JournalKinds.TreeCrud,
                    ProductId = product.Id.ToString(),
                });
        }

        public async Task AddProductToFolder(Guid productId, Guid folderId, InitiatorInfo initiator)
        {
            if (!TryGetValue(folderId, out var folder))
            {
                _logger.Warn($"AddProductToFolder: folder to '{folderId}' not found.");
                return;
            }

            if (await TryUpdateProductInFolder(productId, folder, initiator))
            {
                await GrantFolderRoles(folder, productId);

                _logger.Info($"AddProductToFolder: Product '{productId}' is added to folder '{folder.Name}' by '{initiator}'");
            }
            else
                _logger.Warn($"AddProductToFolder: TryUpdateProductInFolder is unsuccess.");

        }

        public async Task RemoveProductFromFolder(Guid productId, Guid folderId, InitiatorInfo initiator)
        {
            if (!TryGetValue(folderId, out var folder))
            {
                _logger.Warn($"RemoveProductFromFolder: folder to '{folderId}' not found.");
                return;
            }

            if (await TryUpdateProductInFolder(productId, folder, initiator, ActionType.Delete))
            {
                await RevokeFolderRoles(folder, productId);

                _logger.Info($"RemoveProductFromFolder: Product '{productId}' is removed from folder '{folder.Name}' by '{initiator}'");
            }
            else
                _logger.Warn($"RemoveProductFromFolder: TryUpdateProductInFolder is unsuccess.");

        }

        public Dictionary<string, string> GetFolderDefaultChats(Guid folderId)
        {
            var chats = new Dictionary<string, string>(1 << 2);

            if (TryGetValue(folderId, out var folder) && folder.DefaultChats.IsCustom)
                foreach (var chat in folder.DefaultChats.SelectedChats)
                    chats.Add(chat.ToString(), GetChatName?.Invoke(chat));

            return chats;
        }

        private async Task GrantFolderRoles(FolderModel folder, Guid productId)
        {
            foreach (var (user, role) in folder.UserRoles)
                if (!user.IsUserProduct(productId))
                {
                    user.ProductsRoles.Add((productId, role));
                    await _userManager.UpdateUser(user);
                }
        }

        private async Task RevokeFolderRoles(FolderModel folder, Guid productId)
        {
            foreach (var (user, role) in folder.UserRoles)
                if (user.ProductsRoles.Remove((productId, role)))
                    await _userManager.UpdateUser(user);
        }

        // keepsFolder: only the settings are updated, the product's folder is
        // left as it is (the first half of a move between two folders).
        private async Task<bool> TryUpdateProductInFolder(Guid productId, FolderModel folder, InitiatorInfo initiator, ActionType action = ActionType.Update, bool keepsFolder = false)
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
                    FolderId = keepsFolder ? null : action is ActionType.Delete ? Guid.Empty : folder.Id,

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

        // #1864: a product stored by an earlier build inherits a folder's calendar preset (a month to a year) without
        // it: as FromFolder with 0 ticks after a change of the folder, which never expires, or with the preset's
        // nominal length in ticks when saved as "from folder". Both are re-derived here from the folder's present
        // setting and stored with the product, so the inherited preset applies as the same preset set on the product.
        private async Task ResolveInheritedCalendarPresets()
        {
            static TimeIntervalModel Resolve(TimeIntervalModel current, TimeIntervalViewModel folderSetting)
            {
                if (current is null || !current.IsFromFolder || !folderSetting.TimeInterval.IsDynamic())
                    return null;

                var inherited = folderSetting.ToModel().ToFromFolderModel();

                return inherited.FolderPreset == current.FolderPreset ? null : inherited;
            }

            foreach (var product in _cache.GetProducts())
                if (product.FolderId.HasValue && TryGetValueById(product.FolderId, out var folder))
                {
                    var update = new ProductUpdate
                    {
                        Id = product.Id,
                        TTL = Resolve(product.Settings.TTL.CurValue, folder.TTL),
                        KeepHistory = Resolve(product.Settings.KeepHistory.CurValue, folder.KeepHistory),
                        SelfDestroy = Resolve(product.Settings.SelfDestroy.CurValue, folder.SelfDestroy),
                        Initiator = InitiatorInfo.AsSystemMigrator(),
                    };

                    if (update.TTL != null || update.KeepHistory != null || update.SelfDestroy != null)
                    {
                        _logger.Info($"Product '{product.DisplayName}' [{product.Id}] inherits a calendar preset from folder '{folder.Name}' that was not applied, resolved (#1864):" +
                                     $"{(update.KeepHistory != null ? $" keep history {update.KeepHistory}" : string.Empty)}" +
                                     $"{(update.SelfDestroy != null ? $" remove after inactivity {update.SelfDestroy}" : string.Empty)}" +
                                     $"{(update.TTL != null ? $" TTL {update.TTL}" : string.Empty)}");

                        await _cache.UpdateProductAsync(update);
                    }
                }
        }

        private List<FolderModel> GetFolders() => Values.Select(x => x.RecalculateState()).ToList();


        private string GetChatNameById(Guid id) => GetChatName?.Invoke(id);

        private IEnumerable<Guid> GetGlobalChatIds()
        {
            foreach (var chat in _chatsManager.GetValues())
                if (chat.Folders.Count == 0)
                    yield return chat.Id;
        }

        private bool IsChatGlobal(Guid id) =>
            _chatsManager.TryGetValue(id, out var chat) && chat.Folders.Count == 0;

        private Task FanOutRemoveFolderFromChats(Guid folderId, List<Guid> removedChats, InitiatorInfo initiator)
        {
            if (RemoveFolderFromChats is null)
                return Task.CompletedTask;

            var tasks = RemoveFolderFromChats.GetInvocationList()
                .Cast<Func<Guid, List<Guid>, InitiatorInfo, Task>>()
                .Select(h => h(folderId, removedChats, initiator));

            return Task.WhenAll(tasks);
        }
    }
}
