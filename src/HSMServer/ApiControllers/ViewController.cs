using HSMServer.Core.Authentication;
using HSMServer.Core.Model.Authentication;
using HSMServer.Model.TreeViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.ApiControllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [ApiController]
    [Route("api/[controller]")]
    public class ViewController : Controller
    {
        private readonly TreeViewModel _treeViewModel;
        private readonly IUserManager _userManager;

        public ViewController(TreeViewModel treeViewModel, IUserManager userManager)
        {
            _treeViewModel = treeViewModel;
            _userManager = userManager;
        }

        [HttpGet(nameof(GetAllProducts))]
        public ActionResult<Dictionary<string, string>> GetAllProducts()
        {
            return _treeViewModel.Nodes.Values.ToDictionary(product => product.Id, product => product.Name);
        }

        [HttpGet(nameof(GetUsersNotAdmin))]
        public ActionResult<List<User>> GetUsersNotAdmin()
        {
            return _userManager.GetUsers(u => !u.IsAdmin).ToList();
        }
    }
}
