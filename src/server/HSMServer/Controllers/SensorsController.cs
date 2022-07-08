using HSM.Core.Monitoring;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Cache;
using HSMServer.Core.Converters;
using HSMServer.Core.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text;
using SensorType = HSMSensorDataObjects.SensorType;

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

                if (CanAddToQueue(BuildStoreInfo(sensorValue, sensorValue.Convert()),
                    out var message))
                    return Ok(sensorValue);

                return StatusCode(406, message);
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

                if (CanAddToQueue(BuildStoreInfo(sensorValue, sensorValue.Convert()),
                    out var message))
                    return Ok(sensorValue);

                return StatusCode(406, message);
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

                if (CanAddToQueue(BuildStoreInfo(sensorValue, sensorValue.Convert()),
                    out var message))
                    return Ok(sensorValue);

                return StatusCode(406, message);
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

                if (CanAddToQueue(BuildStoreInfo(sensorValue, sensorValue.Convert()),
                    out var message))
                    return Ok(sensorValue);

                return StatusCode(406, message);
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

                if (CanAddToQueue(BuildStoreInfo(sensorValue, sensorValue.Convert()),
                    out var message))
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

                if (CanAddToQueue(BuildStoreInfo(sensorValue, sensorValue.Convert()),
                    out var message))
                    return Ok(sensorValue);

                return StatusCode(406, message);
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

                if (CanAddToQueue(BuildStoreInfo(sensorValue, sensorValue.ConvertToFileSensorBytes().Convert()),
                    out var message))
                    return Ok(sensorValue);

                return StatusCode(406, message);
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

                if (CanAddToQueue(BuildStoreInfo(sensorValue, sensorValue.Convert()),
                    out var message))
                    return Ok(sensorValue);

                return StatusCode(406, message);
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
            if (values == null || !values.Any())
                return BadRequest(values);

            try
            {
                var valuesList = values.ToList();

                _dataCollector.ReportSensorsCount(valuesList.Count);

                var result = new Dictionary<string, string>(values.Count());
                foreach (var value in valuesList)
                {
                    var sensorValue = value.Convert();

                    if (!CanAddToQueue(BuildStoreInfo(sensorValue, sensorValue.Convert()), out var message))
                        result[sensorValue.Key] = message;
                }

                return result.Count == 0 ? Ok(values) : StatusCode(406, result);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data");
                return BadRequest(values);
            }
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

                var result = new Dictionary<string, string>(values.Count);
                foreach (var value in values)
                {
                    BaseValue convertedValue = value.Type switch
                    {
                        SensorType.BooleanSensor => value.ConvertToBool(),
                        SensorType.DoubleSensor => value.ConvertToDouble(),
                        SensorType.IntSensor => value.ConvertToInt(),
                        SensorType.StringSensor => value.ConvertToString(),
                        SensorType.IntegerBarSensor => value.ConvertToIntBar(),
                        SensorType.DoubleBarSensor => value.ConvertToDoubleBar(),
                        _ => null
                    };
                    var storeInfo = new StoreInfo
                    {
                        Key = value.Key,
                        Path = value.Path,
                        BaseValue = convertedValue
                    };

                    if (!CanAddToQueue(storeInfo, out var message))
                        result[storeInfo.Key] = message;
                }

                return result.Count == 0 ? Ok(values) : StatusCode(406, result);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data");
                return BadRequest(values);
            }
        }


        private bool CanAddToQueue(StoreInfo storeInfo,
            out string message)
        {
            if (_cache.TryCheckKeyPermissions(storeInfo, out message))
            {
                _updatesQueue.AddItem(storeInfo);
                return true;
            }

            return false;
        }

        private StoreInfo BuildStoreInfo(SensorValueBase valueBase, BaseValue baseValue)
        {
            return new StoreInfo
            {
                Key = valueBase.Key,
                Path = valueBase.Path,
                BaseValue = baseValue
            };
        }
    }
}
