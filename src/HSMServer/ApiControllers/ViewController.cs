using HSMServer.Core.Authentication;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace HSMServer.ApiControllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [ApiController]
    [Route("api/[controller]")]
    public class ViewController : Controller
    {
        private readonly IMonitoringCore _monitoringCore;
        private readonly IUserManager _userManager;

        public ViewController(IMonitoringCore monitoringCore, IUserManager userManager)
        {
            _monitoringCore = monitoringCore;
            _userManager = userManager;
        }

        [HttpGet(nameof(GetAllProducts))]
        public ActionResult<List<Product>> GetAllProducts()
        {
            return _monitoringCore.GetAllProducts();
        }

        [HttpGet(nameof(GetUsersNotAdmin))]
        public ActionResult<List<User>> GetUsersNotAdmin()
        {
            return _userManager.GetUsersNotAdmin();
        }
    }
}
