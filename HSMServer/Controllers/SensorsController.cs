using System;
using HSMServer.DataLayer;
using HSMServer.Model;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace HSMServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SensorsController : ControllerBase
    {
        private readonly Logger _logger;
        private readonly IMonitoringCore _monitoringCore;
        public SensorsController(MonitoringCore monitoringCore)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _monitoringCore = monitoringCore;
            _logger.Info("Sensors controller started");
        }
        
        [HttpPost("")]
        public ActionResult<JobResult> Post([FromBody] JobResult jobResult)
        {
            try
            {
                _monitoringCore.AddSensorInfo(jobResult);
                return Ok(jobResult);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to put data!");
                return BadRequest(jobResult);
            }
        }

        [HttpPost("nokey")]
        public ActionResult<string> Post([FromBody] NewJobResult newJobResult)
        {
            try
            {
                return _monitoringCore.AddSensorInfo(newJobResult);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to add new sensor!");
                return e.Message.ToString();
            }
        }

        [HttpPost("string")]
        public ActionResult<string> Post([FromBody] string serialized)
        {
            return Ok(serialized);
        }
    }
}
