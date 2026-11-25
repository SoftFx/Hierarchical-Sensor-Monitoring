using HSMServer.Authentication;
using HSMServer.Filters.FolderRoleFilters;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    public class FoldersController : BaseController
    {
        private readonly IChatsManager _chatsManager;
        private readonly IFolderManager _folderManager;
        private readonly TreeViewModel _tree;


        public FoldersController(IFolderManager folderManager, IUserManager userManager,
            TreeViewModel treeViewModel, IChatsManager chatsManager) : base(userManager)
        {
            _chatsManager = chatsManager;
            _folderManager = folderManager;
            _tree = treeViewModel;
        }


        [HttpGet]
        [FolderRoleFilterByFolderId(nameof(folderId), ProductRoleEnum.ProductManager)]
        public IActionResult EditFolder(Guid? folderId)
        {
            return folderId == null
                ? View(new EditFolderViewModel(BuildFolderProducts()))
                : View(BuildEditFolder(folderId.Value));
        }

        [HttpPost]
        [FolderRoleFilterByEditModel(nameof(editFolder), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> EditFolder(EditFolderViewModel editFolder)
        {
            if (!_folderManager.TryGetValue(editFolder.Id, out var folder))
                return NotFound();

            var products = editFolder.GetFolderProducts(_tree);

            if (DenyProductsChange(folder, products) is { } denied)
                return denied;

            if (!ModelState.IsValid)
            {
                var invalidFolder = BuildEditFolder(editFolder.Id);

                invalidFolder.Name = editFolder.Name;
                invalidFolder.Color = editFolder.Color;
                invalidFolder.Description = editFolder.Description;
                invalidFolder.Products = BuildFolderProducts(invalidFolder.Products?.SelectedProducts);

                return View(nameof(EditFolder), invalidFolder);
            }

            var oldProducts = new Dictionary<Guid, ProductNodeViewModel>(folder.Products);

            folder.Products.Clear();
            foreach (var product in products)
                folder.Products.Add(product.Id, product);

            if (await _folderManager.TryUpdate(editFolder.ToFolderUpdate(CurrentInitiator)))
            {
                foreach (var (productId, _) in oldProducts.Except(folder.Products))
                    await _folderManager.RemoveProductFromFolder(productId, folder.Id, CurrentInitiator);

                foreach (var (productId, _) in folder.Products.Except(oldProducts))
                    await _folderManager.AddProductToFolder(productId, folder.Id, CurrentInitiator);
            }

            return View(nameof(EditFolder), BuildEditFolder(folder.Id));
        }

        [HttpPost]
        // Folders are created by admins: the page opens in "add" mode for them alone (the GET above
        // refuses a request without a folder id to everyone else). The role filter this action used to
        // carry judged the folder id of the form, which a new folder does not have and the action does
        // not read, so a Manager of any folder who posted that folder's id passed it and created a
        // folder holding whatever products the form listed.
        public async Task<IActionResult> AddFolder(EditFolderViewModel editFolder)
        {
            if (!CurrentUser.IsAdmin)
                return StatusCode(StatusCodes.Status403Forbidden);

            if (DenyProductsChange(folder: null, editFolder.GetFolderProducts(_tree)) is { } denied)
                return denied;

            if (!ModelState.IsValid)
            {
                editFolder.Products = BuildFolderProducts(editFolder.Products?.SelectedProducts);

                return View(nameof(EditFolder), editFolder);
            }

            await _folderManager.TryAdd(editFolder.ToFolderAdd(CurrentUser, CurrentInitiator, _tree), out var newFolder);

            return RedirectToAction(nameof(EditFolder), new { folderId = newFolder.Id });
        }

        [HttpPost]
        [FolderRoleFilterByFolderId(nameof(folderId), ProductRoleEnum.ProductManager)]
        public Task RemoveFolder(Guid folderId) => _folderManager.TryRemove(new(folderId, CurrentInitiator));


        [HttpPost]
        [FolderRoleFilterByEditSettings(nameof(folderSettings), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> EditSettings(FolderSettingsViewModel folderSettings)
        {
            var update = new FolderUpdate()
            {
                Id = folderSettings.Id,
                TTL = folderSettings.ExpectedUpdateInterval,
                KeepHistory = folderSettings.SavedHistoryPeriod,
                SelfDestroy = folderSettings.SelfDestoryPeriod,
                Initiator = CurrentInitiator
            };

            await _folderManager.TryUpdate(update);

            return PartialView("_Settings", new FolderSettingsViewModel(_folderManager[update.Id]));
        }


        [HttpPost]
        // The Chats tab belongs to the edit page, which opens for the folder's Managers only, but this
        // action was the one write of the page without a role filter: any signed-in user could rebind
        // the alert chats of any folder. The check is made here, not by a filter, so that an unknown
        // folder id is answered 404 before FolderManager.TryUpdate dereferences it.
        public async Task<IActionResult> UpdateChats(FolderChatsViewModel viewModel)
        {
            if (!_folderManager.TryGetValue(viewModel.FolderId, out _))
                return NotFound();

            if (!CurrentUser.IsFolderManager(viewModel.FolderId))
                return StatusCode(StatusCodes.Status403Forbidden);

            var connected = viewModel.ConnectedChatIds?.Where(id => id != Guid.Empty).ToList() ?? new();
            if (viewModel.NewChats is not null)
                connected.AddRange(viewModel.NewChats);

            var update = new FolderUpdate()
            {
                Id = viewModel.FolderId,
                DefaultChats = viewModel.DefaultChats,
                Chats = new HashSet<Guid>(connected),
                Initiator = CurrentInitiator
            };

            await _folderManager.TryUpdate(update);

            return PartialView("_Chats", BuildFolderChats(_folderManager[viewModel.FolderId]));
        }


        [HttpGet]
        public IActionResult ResetUsers(Guid folderId) => GetUsersPartialView(_folderManager[folderId]);

        [HttpPost]
        [FolderRoleFilterByUserRights(nameof(model), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> AddUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager[model.UserId];
            var folder = _folderManager[model.EntityId];

            user.FoldersRoles.Add(folder.Id, model.Role);

            foreach (var productId in folder.Products.Keys)
                if (!user.IsUserProduct(productId))
                    user.ProductsRoles.Add((productId, model.Role));

            if (await _userManager.UpdateUser(user))
                folder.UserRoles.Add(user, model.Role);

            return GetUsersPartialView(folder);
        }

        [HttpPost]
        [FolderRoleFilterByUserRights(nameof(model), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> EditUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager[model.UserId];
            var folder = _folderManager[model.EntityId];

            if (user.FoldersRoles.TryGetValue(folder.Id, out var oldUserRole))
            {
                user.FoldersRoles[folder.Id] = model.Role;

                foreach (var productId in folder.Products.Keys)
                    if (user.ProductsRoles.Remove((productId, oldUserRole)))
                        user.ProductsRoles.Add((productId, model.Role));

                if (await _userManager.UpdateUser(user))
                    folder.UserRoles[user] = model.Role;
            }

            return GetUsersPartialView(folder);
        }

        [HttpPost]
        [FolderRoleFilterByUserRights(nameof(model), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> RemoveUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager[model.UserId];
            var folder = _folderManager[model.EntityId];

            user.FoldersRoles.Remove(folder.Id);

            foreach (var productId in folder.Products.Keys)
                user.ProductsRoles.Remove((productId, model.Role));

            // TODO: also call user.Notifications.RemoveSensor for sensors that are not available for user 

            if (await _userManager.UpdateUser(user))
                folder.UserRoles.Remove(user);

            return GetUsersPartialView(folder);
        }

        private PartialViewResult GetUsersPartialView(FolderModel folder) => PartialView("_Users", BuildFolderUsers(folder));


        private EditFolderViewModel BuildEditFolder(Guid folderId)
        {
            var folder = _folderManager[folderId];

            return new(folder, BuildFolderProducts(), BuildFolderUsers(folder), BuildFolderChats(folder));
        }

        // The gate of the product list a folder form posts. A folder gives every user of it their folder
        // role on a product that enters it and takes that role away when the product leaves
        // (FolderManager.AddProductToFolder, RemoveProductFromFolder), so the list is a list of role
        // grants, and the folder role alone does not cover it: the ids come from the client and are
        // resolved against the whole tree. Each product that enters or leaves must be one the caller
        // manages (admin, or Manager of that product), and a product may enter only as a root product
        // that sits in no other folder, which is all the page offers. Null when the list is acceptable,
        // otherwise the answer: 403 for a product the caller does not manage, 400 for one that cannot
        // enter. The request is refused as a whole; nothing of it is applied.
        private IActionResult DenyProductsChange(FolderModel folder, List<ProductNodeViewModel> requested)
        {
            var current = folder?.Products.Keys.ToHashSet() ?? [];
            var entering = requested.Where(p => !current.Contains(p.Id)).ToList();
            var leaving = current.Except(requested.Select(p => p.Id));

            if (!entering.Select(p => p.Id).Concat(leaving).All(CurrentUser.IsManager))
                return StatusCode(StatusCodes.Status403Forbidden);

            // Parent is the folder of a root product that has one, and the parent node of a nested node.
            if (entering.Any(p => p.Parent is not null && !ReferenceEquals(p.Parent, folder)))
                return BadRequest();

            return null;
        }

        // The products the page offers to add are the ones DenyProductsChange lets in.
        private FolderProductsViewModel BuildFolderProducts(List<string> selectedProducts = null)
        {
            var availableProducts = _tree.GetUserProducts(CurrentUser).Where(p => p.FolderId is null && CurrentUser.IsManager(p.Id)).ToList();

            return new(availableProducts, selectedProducts);
        }

        private FolderUsersViewModel BuildFolderUsers(FolderModel folder) =>
            new(folder, _userManager.GetUsers(u => !u.IsAdmin));

        private FolderChatsViewModel BuildFolderChats(FolderModel folder) =>
            new(folder, _chatsManager.GetValues().ToList());
    }
}
