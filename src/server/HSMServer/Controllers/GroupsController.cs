using HSMServer.Extensions;
using HSMServer.Groups;
using HSMServer.Model.Groups;
using Microsoft.AspNetCore.Mvc;
using System;

namespace HSMServer.Controllers
{
    public class GroupsController : Controller
    {
        private IGroupManager _groupManager;


        public GroupsController(IGroupManager groupManager)
        {
            _groupManager = groupManager;
        }


        [HttpGet]
        public IActionResult EditGroup(Guid? groupId)
        {
            return View(new GroupViewModel());
        }

        [HttpPost]
        public IActionResult EditGroup(GroupViewModel group)
        {
            return View(new GroupViewModel() { Id = Guid.NewGuid(), Name = group.Name, Description = group.Description, Color = group.Color, Author = "PalinaSh", CreationDate = DateTime.UtcNow.ToDefaultFormat() });
        }
    }
}
