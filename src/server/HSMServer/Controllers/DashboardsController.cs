using System;
using HSMServer.Authentication;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    public class DashboardsController : BaseController
    {
        private readonly TreeViewModel _treeViewModel;


        public DashboardsController(TreeViewModel treeViewModel, IUserManager userManager) : base(userManager)
        {
            _treeViewModel = treeViewModel;
        }


        public IActionResult Index() => View(_treeViewModel);

        [HttpGet]
        public IActionResult GetSource(Guid id)
        {
            if (_treeViewModel.Sensors.TryGetValue(id, out var sensorNodeViewModel))
            {
                return View("Source/Source", sensorNodeViewModel);
            }
            return Json(new
            {
                a = id
            });
        }
    }
}
