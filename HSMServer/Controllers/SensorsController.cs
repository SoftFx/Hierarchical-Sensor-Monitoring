using System;
using HSMSensorDataObjects;
using HSMServer.Model;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Controller for receiving sensors data via https protocol. There is a default product for testing swagger methods. Default product key is
    ///
    ///     2201cd7959dc87a1dc82b8abf29f48
    /// 
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

        ///// <summary>
        ///// Method receives data of simple type, which has boolean result and string comment
        ///// </summary>
        ///// <param name="jobResult"></param>
        ///// <returns></returns>
        //[HttpPost("")]
        //public ActionResult<JobResult> Post([FromBody] JobResult jobResult)
        //{
        //    try
        //    {
        //        //_monitoringCore.AddSensorInfo(jobResult);
        //        return Ok(jobResult);
        //    }
        //    catch (Exception e)
        //    {
        //        _logger.Error(e, "Failed to put data!");
        //        return BadRequest(jobResult);
        //    }
        //}

        /// <summary>
        /// Receives value of bool sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("bool")]
        public ActionResult<BoolSensorValue> Post([FromBody] BoolSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives value of int sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("int")]
        public ActionResult<IntSensorValue> Post([FromBody] IntSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives value of double sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("double")]
        public ActionResult<DoubleSensorValue> Post([FromBody] DoubleSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives value of string sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("string")]
        public ActionResult<StringSensorValue> Post([FromBody] StringSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives value of double bar sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("doubleBar")]
        public ActionResult<DoubleBarSensorValue> Post([FromBody] DoubleBarSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives value of integer bar sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("intBar")]
        public ActionResult<IntBarSensorValue> Post([FromBody] IntBarSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to put data!");
                return BadRequest(sensorValue);
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

        //[HttpPost("string")]
        //public ActionResult<string> Post([FromBody] string serialized)
        //{
        //    return Ok(serialized);
        //}
    }
}
