using HSMServer.Extensions;
using HSMServer.Groups;
using HSMServer.Model.Authentication;
using HSMServer.Model.Groups;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Linq;

namespace HSMServer.Controllers
{
    public class GroupsController : Controller
    {
        private IGroupManager _groupManager;
        private TreeViewModel _treeViewModel;


        public GroupsController(IGroupManager groupManager, TreeViewModel treeViewModel)
        {
            _groupManager = groupManager;
            _treeViewModel = treeViewModel;
        }


        [HttpGet]
        public IActionResult EditGroup(Guid? groupId)
        {
            return View(new GroupViewModel() { AllProducts = _treeViewModel.GetUserProducts(HttpContext.User as User).Select(p => new SelectListItem() { Text = p.Name, Value = p.EncodedId }).ToList() });
        }

        [HttpPost]
        public IActionResult EditGroup(GroupViewModel group)
        {
            return View(new GroupViewModel() { Id = Guid.NewGuid(), Name = group.Name, Description = group.Description, Color = group.Color, Author = "PalinaSh", CreationDate = DateTime.UtcNow.ToDefaultFormat() });
        }
    }
}
