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
        public async Task<IActionResult> EditFolder(EditFolderViewModel folder)
        {
            var existingFolder = _folderManager[folder.Id];
            var oldFolderProducts = existingFolder.Products.ToList();

            existingFolder.Products.Clear();
            existingFolder.Products.AddRange(folder.GetFolderProducts(_treeViewModel));

            if (await _folderManager.TryUpdate(folder.ToFolderUpdate()))
            {
                foreach (var product in oldFolderProducts.Except(existingFolder.Products))
                {
                    _cache.RemoveProductFolder(product.Id);

                    foreach (var (user, role) in existingFolder.UserRoles)
                        if (user.ProductsRoles.Remove((product.Id, role)))
                            await _userManager.UpdateUser(user);
                }

                foreach (var product in existingFolder.Products.Except(oldFolderProducts))
                {
                    _cache.AddProductFolder(product.Id, existingFolder.Id);

                    foreach (var (user, role) in existingFolder.UserRoles)
                        if (!user.ProductsRoles.Any(x => x.Item1 == product.Id))
                        {
                            user.ProductsRoles.Add((product.Id, role));
                            await _userManager.UpdateUser(user);
                        }
                }
            }

            return View(nameof(EditFolder), BuildEditFolder(existingFolder.Id));
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


        [HttpGet]
        public IActionResult ResetUsers(Guid folderId) => GetUsersPartialView(_folderManager[folderId]);

        [HttpPost]
        public async Task<IActionResult> AddUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager[model.UserId];
            var folder = _folderManager[model.EntityId];

            user.FoldersRoles.Add(folder.Id, model.Role);

            foreach (var product in folder.Products)
                if (!user.ProductsRoles.Any(x => x.Item1 == product.Id))
                    user.ProductsRoles.Add((product.Id, model.Role));

            if (await _userManager.UpdateUser(user))
                folder.UserRoles.Add(user, model.Role);

            return GetUsersPartialView(folder);
        }

        [HttpPost]
        public async Task<IActionResult> EditUserRole([FromBody] UserRightViewModel model)
        {
            var user = _userManager[model.UserId];
            var folder = _folderManager[model.EntityId];

            if (user.FoldersRoles.ContainsKey(folder.Id))
            {
                var oldUserRole = user.FoldersRoles[folder.Id];

                user.FoldersRoles[folder.Id] = model.Role;

                foreach (var product in folder.Products)
                    if (user.ProductsRoles.Remove((product.Id, oldUserRole)))
                        user.ProductsRoles.Add((product.Id, model.Role));

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

            foreach (var product in folder.Products)
                user.ProductsRoles.Remove((product.Id, model.Role));

            // TODO: also call user.Notifications.RemoveSensor for sensors that are not available for user 

            if (await _userManager.UpdateUser(user))
                folder.UserRoles.Remove(user);

            return GetUsersPartialView(folder);
        }

        private IActionResult GetUsersPartialView(FolderModel folder) => PartialView("_Users", BuildFolderUsers(folder));


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
