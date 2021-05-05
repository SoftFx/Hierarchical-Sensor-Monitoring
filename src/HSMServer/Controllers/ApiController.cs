using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.MonitoringServerCore;
using HSMService;
using NLog;

namespace HSMServer.Controllers
{
    [Route("api/site")]
    [ApiController]
    public class ApiController : ControllerBase
    {
        private readonly Logger _logger;
        private readonly IMonitoringCore _monitoringCore;
        public ApiController(IMonitoringCore monitoringCore)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _monitoringCore = monitoringCore;
        }

        //[HttpGet("tree")]
        //public ActionResult<SensorsUpdateMessage> Get()
        //{
            
        //}

        //[HttpGet("updates")]
        //public ActionResult<SensorsUpdateMessage> Get()
        //{

        //}
    }
}
