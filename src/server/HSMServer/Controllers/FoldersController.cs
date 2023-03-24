using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
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
                ? View(new EditFolderViewModel(GetUserProducts()))
                : View(new EditFolderViewModel(_folderManager[folderId.Value], GetUserProducts()));
        }

        [HttpPost]
        public async Task<IActionResult> AddFolder(EditFolderViewModel folder)
        {
            var addedProducts = new List<ProductNodeViewModel>();
            foreach (var productId in folder.Products)
                if (Guid.TryParse(productId, out var id) && _treeViewModel.Nodes.TryGetValue(id, out var product))
                    addedProducts.Add(product);

            var newFolder = new FolderModel(folder.ToEntity((HttpContext.User as User).Id));
            newFolder.Products.AddRange(addedProducts);

            if (await _folderManager.TryAdd(newFolder))
                foreach (var product in addedProducts)
                    _cache.UpdateProduct(new ProductUpdate() { Id = product.Id, FolderId = newFolder.Id });

            return View(nameof(EditFolder), new EditFolderViewModel(_folderManager[newFolder.Id], GetUserProducts()));
        }


        private List<ProductNodeViewModel> GetUserProducts() => _treeViewModel.GetUserProducts(HttpContext.User as User);
    }
}
