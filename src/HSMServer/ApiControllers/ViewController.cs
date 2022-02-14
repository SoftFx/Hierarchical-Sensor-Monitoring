using HSMServer.Core.Authentication;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Products;
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
        private readonly IProductManager _productManager;
        private readonly IUserManager _userManager;

        public ViewController(IProductManager productManager, IUserManager userManager)
        {
            _productManager = productManager;
            _userManager = userManager;
        }

        [HttpGet(nameof(GetAllProducts))]
        public ActionResult<List<Product>> GetAllProducts()
        {
            return _productManager.Products;
        }

        [HttpGet(nameof(GetUsersNotAdmin))]
        public ActionResult<List<User>> GetUsersNotAdmin()
        {
            return _userManager.GetUsers(u => !u.IsAdmin).ToList();
        }
    }
}
