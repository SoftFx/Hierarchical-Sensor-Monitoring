using HSMSensorDataObjects;
using HSMSensorDataObjects.HistoryRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.BackgroundTask;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Extensions;
using HSMServer.ModelBinders;
using HSMServer.ObsoleteUnitedSensorValue;
using HSMServer.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SensorType = HSMSensorDataObjects.SensorType;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Controller for receiving sensors data via https protocol. There is a default product for testing swagger methods.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class SensorsController : ControllerBase
    {
        private readonly ILogger<SensorsController> _logger;
        private readonly IUpdatesQueue _updatesQueue;
        private readonly DataCollectorWrapper _dataCollector;
        private readonly ITreeValuesCache _cache;


        public SensorsController(IUpdatesQueue updatesQueue, DataCollectorWrapper dataCollector,
            ILogger<SensorsController> logger, ITreeValuesCache cache)
        {
            _updatesQueue = updatesQueue;
            _dataCollector = dataCollector;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// Receives value of bool sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("bool")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<BoolSensorValue> Post([FromBody] BoolSensorValue sensorValue)
        {
            try
            {
                _dataCollector.ReceivedDataCountSensor.AddValue(1);

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
                _dataCollector.ReceivedDataCountSensor.AddValue(1);

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
                _dataCollector.ReceivedDataCountSensor.AddValue(1);

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
                _dataCollector.ReceivedDataCountSensor.AddValue(1);

                if (CanAddToQueue(BuildStoreInfo(sensorValue, sensorValue.Convert()), out var message))
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
        /// Receives value of timespan sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("timespan")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<TimeSpanSensorValue> Post([FromBody] TimeSpanSensorValue sensorValue)
        {
            try
            {
                _dataCollector.ReceivedDataCountSensor.AddValue(1);

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
        /// Receives value of version sensor
        /// </summary>
        /// <param name="sensorValue"></param>
        /// <returns></returns>
        [HttpPost("version")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<VersionSensorValue> Post([FromBody] VersionSensorValue sensorValue)
        {
            try
            {
                _dataCollector.ReceivedDataCountSensor.AddValue(1);

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
                _dataCollector.ReceivedDataCountSensor.AddValue(1);

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
                _dataCollector.ReceivedDataCountSensor.AddValue(1);

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
        /// Receives the value of file sensor, where the file contents are presented as byte array.
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
                _dataCollector.ReceivedDataCountSensor.AddValue(1);

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
        /// Accepts data in SensorValueBase format. Converts data to a typed format and saves it to the database.
        /// The key must be unique and stored in the header.
        /// </summary>
        /// <param name="values"></param>
        /// <returns></returns>
        [HttpPost("list")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<List<SensorValueBase>> Post([FromBody, ModelBinder(typeof(SensorValueModelBinder))] List<SensorValueBase> values)
        {
            try
            {
                _dataCollector.ReceivedDataCountSensor.AddValue(values.Count);

                var result = new Dictionary<string, string>(values.Count);
                foreach (var value in values)
                {
                    var storeInfo = BuildStoreInfo(value, value.Convert());

                    if (!CanAddToQueue(storeInfo, out var message))
                        result[storeInfo.Path] = message;
                }

                return result.Count == 0 ? Ok(values) : StatusCode(406, result);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data");
                return BadRequest(values);
            }
        }

        /// <summary>
        /// Obsolete method. Will be removed.
        /// Accepts data in UnitedSensorValue format. Converts data to a typed format and saves it to the database.
        /// </summary>
        /// <param name="values"></param>
        /// <returns></returns>
        [HttpPost("listNew")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public ActionResult<List<UnitedSensorValue>> Post([FromBody] List<UnitedSensorValue> values)
        {
            if (values == null || values.Count == 0)
                return BadRequest();

            try
            {
                _dataCollector.ReceivedDataCountSensor.AddValue(values.Count);

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
                    var storeInfo = new StoreInfo(value.Key, value.Path)
                    {
                        BaseValue = convertedValue
                    };

                    if (!CanAddToQueue(storeInfo, out var message))
                        result[storeInfo.Path] = message;
                }
                return result.Count == 0 ? Ok(values) : StatusCode(406, result);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to put data");
                return BadRequest(values);
            }
        }

        /// <summary>
        /// Get history [from, to] or [from - count] for some sensor
        /// </summary>
        [HttpPost("history")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public async Task<ActionResult<string>> Get([FromBody] HistoryRequest request)
        {
            try
            {
                if (TryCheckReadHistoryRequest(request, out var requestModel, out var message))
                {
                    var historyValues = await _cache.GetSensorValues(requestModel).Flatten();
                    var response = JsonSerializer.Serialize(historyValues.Convert());

                    return Ok(response);
                }

                return StatusCode(406, message);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get history!");
                return BadRequest(request);
            }
        }

        /// <summary>
        /// Get file (csv or txt) history [from, to] or [from - count] for some sensor
        /// </summary>
        [HttpPost("historyFile")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        public async Task<IActionResult> Get([FromBody] FileHistoryRequest request) //TODO merge with ExportHistory History controller
        {
            try
            {
                if (TryCheckReadHistoryRequest(request, out var requestModel, out var message))
                {
                    var historyValues = await _cache.GetSensorValues(requestModel).Flatten();
                    var response = historyValues.ConvertToCsv();

                    return request.IsZipArchive
                           ? File(response.CompressToZip(request.FileName, request.Extension), $"{request.FileName}.zip".GetContentType())
                           : File(Encoding.UTF8.GetBytes(response), $"{request.FileName}.{request.Extension}".GetContentType());
                }

                return StatusCode(406, message);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get history!");
                return BadRequest(request);
            }
        }


        private bool CanAddToQueue(StoreInfo storeInfo, out string message)
        {
            if (storeInfo.TryCheckRequest(out message) &&
                _cache.TryCheckKeyWritePermissions(storeInfo, out message))
            {
                _updatesQueue.AddItem(storeInfo);
                return true;
            }

            return false;
        }

        private bool TryCheckReadHistoryRequest(HistoryRequest request, out HistoryRequestModel requestModel, out string message)
        {
            Request.Headers.TryGetValue(nameof(BaseRequest.Key), out var key);

            requestModel = request.Convert(key);

            return request.TryValidate(out message) &&
                   requestModel.TryCheckRequest(out message) &&
                   _cache.TryCheckKeyReadPermissions(requestModel, out message);
        }

        private StoreInfo BuildStoreInfo(SensorValueBase valueBase, BaseValue baseValue)
        {
            Request.Headers.TryGetValue(nameof(BaseRequest.Key), out var key);

            if (string.IsNullOrEmpty(key))
                key = valueBase.Key;

            return new(key, valueBase.Path) { BaseValue = baseValue };
        }
    }
}
