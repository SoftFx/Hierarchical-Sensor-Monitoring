using HSM.Core.Monitoring;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Cache;
using HSMServer.Core.Converters;
using HSMServer.Core.SensorsUpdatesQueue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;

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
    [AllowAnonymous]
    public class SensorsController : ControllerBase
    {
        private readonly ILogger<SensorsController> _logger;
        private readonly IUpdatesQueue _updatesQueue;
        private readonly IDataCollectorFacade _dataCollector;
        private readonly ITreeValuesCache _cache;


        public SensorsController(IUpdatesQueue updatesQueue, IDataCollectorFacade dataCollector,
            ILogger<SensorsController> logger, ITreeValuesCache cache)
        {
            _updatesQueue = updatesQueue;
            _dataCollector = dataCollector;
            _logger = logger;
            _cache = cache;
        }


        [HttpPost("bool")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<BoolSensorValue> Post([FromBody] BoolSensorValue sensorValue)
        {
            try
            {
                _dataCollector.ReportSensorsCount(1);

                if (CanAddToQueue(sensorValue))
                    return Ok(sensorValue);

                return StatusCode(406, sensorValue);
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
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<IntSensorValue> Post([FromBody] IntSensorValue sensorValue)
        {
            try
            {
                _dataCollector.ReportSensorsCount(1);

                if (CanAddToQueue(sensorValue))
                    return Ok(sensorValue);

                return StatusCode(406, sensorValue);
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
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<DoubleSensorValue> Post([FromBody] DoubleSensorValue sensorValue)
        {
            try
            {
                _dataCollector.ReportSensorsCount(1);

                if (CanAddToQueue(sensorValue))
                    return Ok(sensorValue);

                return StatusCode(406, sensorValue);
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
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<StringSensorValue> Post([FromBody] StringSensorValue sensorValue)
        {
            try
            {
                _dataCollector.ReportSensorsCount(1);

                if (CanAddToQueue(sensorValue))
                    return Ok(sensorValue);

                return StatusCode(406, sensorValue);
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
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<DoubleBarSensorValue> Post([FromBody] DoubleBarSensorValue sensorValue)
        {
            try
            {
                _dataCollector.ReportSensorsCount(1);

                if (CanAddToQueue(sensorValue))
                    return Ok(sensorValue);

                return StatusCode(406, sensorValue);
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
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<IntBarSensorValue> Post([FromBody] IntBarSensorValue sensorValue)
        {
            try
            {
                _dataCollector.ReportSensorsCount(1);

                if (CanAddToQueue(sensorValue))
                    return Ok(sensorValue);

                return StatusCode(406, sensorValue);
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
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<FileSensorValue> Post([FromBody] FileSensorValue sensorValue)
        {
            try
            {
                _dataCollector.ReportSensorsCount(1);

                if (CanAddToQueue(sensorValue.ConvertToFileSensorBytes()))
                    return Ok(sensorValue);

                return StatusCode(406, sensorValue);
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
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<FileSensorBytesValue> Post([FromBody] FileSensorBytesValue sensorValue)
        {
            try
            {
                _dataCollector.ReportSensorsCount(1);

                if (CanAddToQueue(sensorValue))
                    return Ok(sensorValue);

                return StatusCode(406, sensorValue);
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
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<List<CommonSensorValue>> Post([FromBody] IEnumerable<CommonSensorValue> values)
        {
            if (values != null)
            {
                try
                {
                    var valuesList = values.ToList();

                    _dataCollector.ReportSensorsCount(valuesList.Count);
                    //ToDo
                    _updatesQueue.AddItems(valuesList.Select(v => v.Convert()).ToList());

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


        [HttpPost("listNew")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public ActionResult<List<UnitedSensorValue>> Post([FromBody] List<UnitedSensorValue> values)
        {
            if (values == null || values.Count == 0)
                return BadRequest();

            try
            {
                _dataCollector.ReportSensorsCount(values.Count);
                //ToDo
                _updatesQueue.AddItems(values.Cast<SensorValueBase>().ToList());

                return Ok(values);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data");
                return BadRequest(values);
            }
        }


        private bool CanAddToQueue(SensorValueBase value)
        {
            if (_cache.IsValidKey(value.Key))
            {
                _updatesQueue.AddItem(value);
                return true;
            }

            return false;
        }
    }
}
