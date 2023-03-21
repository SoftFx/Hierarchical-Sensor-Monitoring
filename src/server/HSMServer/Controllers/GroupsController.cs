using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Groups;
using HSMServer.Model.Authentication;
using HSMServer.Model.Groups;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Linq;
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
            return View(new GroupViewModel() { AllProducts = _treeViewModel.GetUserProducts(HttpContext.User as User).Select(p => new SelectListItem() { Text = p.Name, Value = p.Id.ToString() }).ToList() });
        }

        [HttpPost]
        public async Task<IActionResult> AddGroup(GroupViewModel group)
        {
            var addedProducts = group.Products.Select(Guid.Parse).ToList();
            var newGroup = new GroupModel()
            {
                AuthorId = (HttpContext.User as User).Id,
                Name = group.Name,
                Color = group.Color,
                Description = group.Description,
                ProductIds = addedProducts,
            };

            if (await _groupManager.TryAdd(newGroup))
                foreach (var productId in addedProducts)
                    _cache.UpdateProduct(new ProductUpdate() { Id = productId, GroupId = newGroup.Id });

            return View(nameof(EditGroup), new GroupViewModel(_groupManager[newGroup.Id]));
        }
    }
}
