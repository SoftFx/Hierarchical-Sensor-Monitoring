using System;
using System.Collections.Generic;
using System.Net.Mime;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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
        private readonly ILogger<SensorsController> _logger;
        private readonly IMonitoringCore _monitoringCore;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="monitoringCore"></param>
        public SensorsController(IMonitoringCore monitoringCore, ILogger<SensorsController> logger)
        {
            _logger = logger;
            _monitoringCore = monitoringCore;
        }

        /// <summary>
        /// Receives value of bool sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        //[HttpPost("bool")]
        //[Consumes(MediaTypeNames.Application.Json)]
        //[ProducesResponseType(StatusCodes.Status200OK)]
        //[ProducesResponseType(StatusCodes.Status400BadRequest)]
        //public async Task<IActionResult> Post([FromBody] BoolSensorValue sensorValue)
        //{
        //    try
        //    {
        //        await _monitoringCore.AddSensorValueAsync(sensorValue);
        //        return Ok(sensorValue);
        //    }
        //    catch (Exception e)
        //    {
        //        _logger.LogError(e, "Failed to put data!");
        //        return BadRequest(sensorValue);
        //    }
        //}

        [HttpPost("bool")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<BoolSensorValue> Post([FromBody] BoolSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives value of int sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("int")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<IntSensorValue> Post([FromBody] IntSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives value of double sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("double")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<DoubleSensorValue> Post([FromBody] DoubleSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives value of string sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("string")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<StringSensorValue> Post([FromBody] StringSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives value of double bar sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("doubleBar")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<DoubleBarSensorValue> Post([FromBody] DoubleBarSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives value of integer bar sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("intBar")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<IntBarSensorValue> Post([FromBody] IntBarSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives the value of file sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("file")]
        [RequestSizeLimit(41943040)]//make limit up to 40 MB
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<FileSensorValue> Post([FromBody] FileSensorValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }

        /// <summary>
        /// Receives the value of file sensor, where the file contents are presented as byte array.
        /// Recommended to use for pdf files in order to keep the pdf file encoding.
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("fileBytes")]
        [RequestSizeLimit(41943040)]//make limit up to 40 MB
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<FileSensorBytesValue> Post([FromBody] FileSensorBytesValue sensorValue)
        {
            try
            {
                _monitoringCore.AddSensorValue(sensorValue);
                return Ok(sensorValue);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data!");
                return BadRequest(sensorValue);
            }
        }
        /// <summary>
        /// Endpoint used by HSMDataCollector services, which sends data in portions
        /// </summary>
        /// <param name="values"></param>
        /// <returns></returns>
        [HttpPost("list")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<List<CommonSensorValue>> Post([FromBody] IEnumerable<CommonSensorValue> values)
        {
            if (values != null)
            {
                try
                {
                    _monitoringCore.AddSensorsValues(values);
                    return Ok(values);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to put data");
                    return BadRequest(values);
                }
            }

            return BadRequest(values);
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
        //        _logger.LogError(e, "Failed to add new sensor!");
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
