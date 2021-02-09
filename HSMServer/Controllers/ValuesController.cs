using System;
using HSMServer.Model;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Simple test controller for checking endpoint settings & accessibility
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        private readonly Logger _logger;
        private readonly IMonitoringCore _monitoringCore;
        public ValuesController(IMonitoringCore monitoringCore)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _monitoringCore = monitoringCore;
        }

        [HttpGet]
        public ActionResult<string> Get()
        {
            _logger.Info($"ValuesController: GET at {DateTime.Now.ToShortTimeString()}");
            return $"string {DateTime.Now.ToShortDateString()} : {DateTime.Now.ToShortTimeString()}";
        }

        [HttpPost]
        public ActionResult<string> Post([FromBody]SampleData input)
        {
            _logger.Info($"Received string {input.Data}");
            return Ok(input);
        }
    }
}
