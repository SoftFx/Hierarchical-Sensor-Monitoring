using System;
using HSMServer.Model;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Controller for receiving sensors data via https protocol
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class SensorsController : ControllerBase
    {
        private readonly Logger _logger;
        private readonly IMonitoringCore _monitoringCore;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="monitoringCore"></param>
        public SensorsController(MonitoringCore monitoringCore)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _monitoringCore = monitoringCore;
            _logger.Info("Sensors controller started");
        }
        
        /// <summary>
        /// Method receives data of simple type, which has boolean result and string comment
        /// </summary>
        /// <param name="jobResult"></param>
        /// <returns></returns>
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

        //[HttpPost("nokey")]
        //public ActionResult<string> Post([FromBody] NewJobResult newJobResult)
        //{
        //    try
        //    {
        //        return _monitoringCore.AddSensorInfo(newJobResult);
        //    }
        //    catch (Exception e)
        //    {
        //        _logger.Error(e, "Failed to add new sensor!");
        //        return e.Message.ToString();
        //    }
        //}

        [HttpPost("string")]
        public ActionResult<string> Post([FromBody] string serialized)
        {
            return Ok(serialized);
        }
    }
}
