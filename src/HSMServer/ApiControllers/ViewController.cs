using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model.Authentication;
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
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IUserManager _userManager;

        public ViewController(ITreeValuesCache treeValuesCache, IUserManager userManager)
        {
            _treeValuesCache = treeValuesCache;
            _userManager = userManager;
        }

        [HttpGet(nameof(GetAllProducts))]
        public ActionResult<List<ProductModel>> GetAllProducts()
        {
            return _treeValuesCache.GetTree();
        }

        [HttpGet(nameof(GetUsersNotAdmin))]
        public ActionResult<List<User>> GetUsersNotAdmin()
        {
            return _userManager.GetUsers(u => !u.IsAdmin).ToList();
        }
    }
}
