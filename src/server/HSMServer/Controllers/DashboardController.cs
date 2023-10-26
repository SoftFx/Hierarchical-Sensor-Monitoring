using HSMServer.Authentication;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    public class DashboardController : BaseController
    {
        private readonly TreeViewModel _treeViewModel;


        public DashboardController(TreeViewModel treeViewModel, IUserManager userManager) : base(userManager)
        {
            _treeViewModel = treeViewModel;
        }


        public IActionResult Index() => View(_treeViewModel);
    }
}
