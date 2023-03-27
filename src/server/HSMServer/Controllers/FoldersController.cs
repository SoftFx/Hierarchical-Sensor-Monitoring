using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    public class FoldersController : Controller
    {
        private readonly IFolderManager _folderManager;
        private readonly ITreeValuesCache _cache;
        private readonly TreeViewModel _treeViewModel;


        public FoldersController(IFolderManager folderManager, ITreeValuesCache cache, TreeViewModel treeViewModel)
        {
            _folderManager = folderManager;
            _cache = cache;
            _treeViewModel = treeViewModel;
        }


        [HttpGet]
        public IActionResult EditFolder(Guid? folderId)
        {
            return folderId == null
                ? View(new EditFolderViewModel(BuildFolderProducts()))
                : View(new EditFolderViewModel(_folderManager[folderId.Value], BuildFolderProducts()));
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
                    _cache.UpdateProduct(new ProductUpdate() { Id = product.Id, FolderId = Guid.Empty });
                foreach (var product in existingFolder.Products.Except(oldFolderProducts))
                    _cache.UpdateProduct(new ProductUpdate() { Id = product.Id, FolderId = existingFolder.Id });
            }

            return View(nameof(EditFolder), new EditFolderViewModel(_folderManager[existingFolder.Id], BuildFolderProducts()));
        }

        [HttpPost]
        public async Task<IActionResult> AddFolder(EditFolderViewModel folder)
        {
            if (!ModelState.IsValid)
            {
                folder.Products = BuildFolderProducts(folder.Products?.SelectedProducts);

                return View(nameof(EditFolder), folder);
            }

            var newFolder = new FolderModel(folder.ToFolderAdd(HttpContext.User as User, _treeViewModel));

            if (await _folderManager.TryAdd(newFolder))
                foreach (var product in newFolder.Products)
                    _cache.UpdateProduct(new ProductUpdate() { Id = product.Id, FolderId = newFolder.Id });

            return View(nameof(EditFolder), new EditFolderViewModel(_folderManager[newFolder.Id], BuildFolderProducts()));
        }


        private FolderProductsViewModel BuildFolderProducts(List<string> selectedProducts = null) =>
            new()
            {
                AvailableProducts = _treeViewModel.GetUserProducts(HttpContext.User as User).Where(p => p.FolderId is null).ToList(),
                SelectedProducts = selectedProducts,
            };
    }
}
