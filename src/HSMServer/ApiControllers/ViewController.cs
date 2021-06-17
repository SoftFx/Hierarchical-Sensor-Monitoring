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

        public ViewController(IMonitoringCore monitoringCore)
        {
            _monitoringCore = monitoringCore;
        }

        [HttpGet("GetProducts")]
        public ActionResult<List<Product>> GetProducts()
        {
            return _monitoringCore.GetAllProducts();
        }
    }
}
