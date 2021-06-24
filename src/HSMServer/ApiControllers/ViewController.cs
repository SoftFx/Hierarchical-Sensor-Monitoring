using HSMServer.Authentication;
using HSMServer.DataLayer.Model;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace HSMServer.ApiControllers
{
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

        //[HttpGet("GetAllViewers")]
        //public ActionResult<List<User>> GetAllViewers()
        //{
        //    return _userManager.GetAllViewers();
        //}

        //[HttpGet("GetAllManagers")]
        //public ActionResult<List<User>> GetAllManagers()
        //{
        //    return _userManager.GetAllManagers();
        //}
    }
}
