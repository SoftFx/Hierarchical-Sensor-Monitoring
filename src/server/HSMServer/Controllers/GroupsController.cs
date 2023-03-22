using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Groups;
using HSMServer.Model.Authentication;
using HSMServer.Model.Groups;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    public class GroupsController : Controller
    {
        private IGroupManager _groupManager;
        private ITreeValuesCache _cache;
        private TreeViewModel _treeViewModel;


        public GroupsController(IGroupManager groupManager, ITreeValuesCache cache, TreeViewModel treeViewModel)
        {
            _groupManager = groupManager;
            _cache = cache;
            _treeViewModel = treeViewModel;
        }


        [HttpGet]
        public IActionResult EditGroup(Guid? groupId)
        {
            return groupId == null
                ? View(new EditGroupViewModel(GetUserProducts()))
                : View(new EditGroupViewModel(_groupManager[groupId.Value], GetUserProducts()));
        }

        [HttpPost]
        public async Task<IActionResult> AddGroup(EditGroupViewModel group)
        {
            var addedProducts = new List<ProductModel>();
            foreach (var productId in group.Products)
                if (Guid.TryParse(productId, out var id))
                    addedProducts.Add(_cache.GetProduct(id));

            var newGroup = new GroupModel(group.ToEntity((HttpContext.User as User).Id));
            newGroup.Products.AddRange(addedProducts);

            if (await _groupManager.TryAdd(newGroup))
                foreach (var product in addedProducts)
                    _cache.UpdateProduct(new ProductUpdate() { Id = product.Id, GroupId = newGroup.Id });

            return View(nameof(EditGroup), new EditGroupViewModel(_groupManager[newGroup.Id], GetUserProducts()));
        }


        private List<ProductNodeViewModel> GetUserProducts() => _treeViewModel.GetUserProducts(HttpContext.User as User);
    }
}
