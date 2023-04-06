using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    public class FoldersController : Controller
    {
        private static readonly EmptyResult _emptyResult = new();

        private readonly ITreeValuesCache _cache;
        private readonly IUserManager _userManager;
        private readonly TreeViewModel _treeViewModel;
        private readonly IFolderManager _folderManager;


        public FoldersController(IFolderManager folderManager, ITreeValuesCache cache,
            IUserManager userManager, TreeViewModel treeViewModel)
        {
            _cache = cache;
            _userManager = userManager;
            _treeViewModel = treeViewModel;
            _folderManager = folderManager;
        }


        [HttpGet]
        public IActionResult EditFolder(Guid? folderId)
        {
            return folderId == null
                ? View(new EditFolderViewModel(BuildFolderProducts()))
                : View(BuildEditFolder(folderId.Value));
        }

        [HttpPost]
        public async Task<IActionResult> EditFolder(EditFolderViewModel editFolder)
        {
            var folder = _folderManager[editFolder.Id];
            var oldProducts = new Dictionary<Guid, ProductNodeViewModel>(folder.Products);

            folder.Products.Clear();
            foreach (var product in editFolder.GetFolderProducts(_treeViewModel))
                folder.Products.Add(product.Id, product);

            if (await _folderManager.TryUpdate(editFolder.ToFolderUpdate()))
            {
                foreach (var (productId, _) in oldProducts.Except(folder.Products))
                {
                    _cache.RemoveProductFolder(productId);

                    foreach (var (user, role) in folder.UserRoles)
                        if (user.ProductsRoles.Remove((productId, role)))
                            await _userManager.UpdateUser(user);
                }

                foreach (var (productId, _) in folder.Products.Except(oldProducts))
                {
                    _cache.AddProductFolder(productId, folder.Id);

                    foreach (var (user, role) in folder.UserRoles)
                        if (!user.IsUserProduct(productId))
                        {
                            user.ProductsRoles.Add((productId, role));
                            await _userManager.UpdateUser(user);
                        }
                }
            }

            return View(nameof(EditFolder), BuildEditFolder(folder.Id));
        }

        [HttpPost]
        public async Task<IActionResult> AddFolder(EditFolderViewModel folder)
        {
            if (!ModelState.IsValid)
            {
                folder.Products = BuildFolderProducts(folder.Products?.SelectedProducts);

                return View(nameof(EditFolder), folder);
            }

            var newFolder = await _folderManager.TryAddFolder(folder.ToFolderAdd(HttpContext.User as User, _treeViewModel));

            return View(nameof(EditFolder), BuildEditFolder(newFolder.Id));
        }

        [HttpPost]
        public Task RemoveFolder(Guid folderId) => _folderManager.TryRemoveFolder(folderId);


        [HttpPost]
        public void EditAlerts(FolderAlertsViewModel folderAlerts)
        {

        }


        [HttpGet]
        public IActionResult ResetUsers(Guid folderId) => GetUsersPartialView(_folderManager[folderId]);

        [HttpPost]
        public async Task<IActionResult> AddUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager[model.UserId];
            var folder = _folderManager[model.EntityId];

            user.FoldersRoles.Add(folder.Id, model.Role);

            foreach (var (productId, _) in folder.Products)
                if (!user.IsUserProduct(productId))
                    user.ProductsRoles.Add((productId, model.Role));

            if (await _userManager.UpdateUser(user))
                folder.UserRoles.Add(user, model.Role);

            return GetUsersPartialView(folder);
        }

        [HttpPost]
        public async Task<IActionResult> EditUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager[model.UserId];
            var folder = _folderManager[model.EntityId];

            if (user.FoldersRoles.TryGetValue(folder.Id, out var oldUserRole))
            {
                user.FoldersRoles[folder.Id] = model.Role;

                foreach (var (productId, _) in folder.Products)
                    if (user.ProductsRoles.Remove((productId, oldUserRole)))
                        user.ProductsRoles.Add((productId, model.Role));

                if (await _userManager.UpdateUser(user))
                    folder.UserRoles[user] = model.Role;
            }

            return GetUsersPartialView(folder);
        }

        [HttpPost]
        public async Task<IActionResult> RemoveUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager[model.UserId];
            var folder = _folderManager[model.EntityId];

            user.FoldersRoles.Remove(folder.Id);

            foreach (var (productId, _) in folder.Products)
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

            return new(folder, BuildFolderProducts(), BuildFolderUsers(folder));
        }

        private FolderProductsViewModel BuildFolderProducts(List<string> selectedProducts = null) =>
            new()
            {
                AvailableProducts = _treeViewModel.GetUserProducts(HttpContext.User as User).Where(p => p.FolderId is null).ToList(),
                SelectedProducts = selectedProducts,
            };

        private FolderUsersViewModel BuildFolderUsers(FolderModel folder) =>
            new(folder, _userManager.GetUsers(u => !u.IsAdmin));
    }
}
