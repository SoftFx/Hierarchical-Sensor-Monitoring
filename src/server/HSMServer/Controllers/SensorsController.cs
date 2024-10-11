using HSMCommon.Extensions;
using HSMCommon.TaskResult;
using HSMSensorDataObjects;
using HSMSensorDataObjects.HistoryRequests;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Extensions;
using HSMServer.Middleware;
using HSMServer.Middleware.Telemetry;
using HSMServer.ModelBinders;
using HSMServer.ObsoleteUnitedSensorValue;
using HSMServer.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string InvalidRequest = "Public API request info not found";

        private static readonly TaskResult _invalidRequestResult = new(InvalidRequest);

        private readonly ILogger<SensorsController> _logger;
        private readonly DataCollectorWrapper _collector;
        private readonly TelemetryCollector _telemetry;

        private readonly ITreeValuesCache _cache;
        private readonly IUpdatesQueue _updatesQueue;


        public SensorsController(IUpdatesQueue updatesQueue, DataCollectorWrapper dataCollector, ILogger<SensorsController> logger, ITreeValuesCache cache, TelemetryCollector telemetry)
        {
            _telemetry = telemetry;
            _logger = logger;

            _updatesQueue = updatesQueue;
            _collector = dataCollector;
            _cache = cache;
        }


        [HttpGet("testConnection")]
        public ActionResult TestConnection() => Ok(); //add test


        /// <summary>
        /// Receives value of bool sensor
        /// </summary>
        /// <param name="boolValue"></param>
        /// <returns></returns>
        [HttpPost("bool")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<BoolSensorValue>> Post([FromBody] BoolSensorValue boolValue) => GetAddDataResult(boolValue);


        /// <summary>
        /// Receives value of int sensor
        /// </summary>
        /// <param name="intValue"></param>
        /// <returns></returns>
        [HttpPost("int")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<IntSensorValue>> Post([FromBody] IntSensorValue intValue) => GetAddDataResult(intValue);


        /// <summary>
        /// Receives value of double sensor
        /// </summary>
        /// <param name="doubleValue"></param>
        /// <returns></returns>
        [HttpPost("double")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<DoubleSensorValue>> Post([FromBody] DoubleSensorValue doubleValue) => GetAddDataResult(doubleValue);


        /// <summary>
        /// Receives value of string sensor
        /// </summary>
        /// <param name="stringValue"></param>
        /// <returns></returns>
        [HttpPost("string")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<StringSensorValue>> Post([FromBody] StringSensorValue stringValue) => GetAddDataResult(stringValue);


        /// <summary>
        /// Receives value of timespan sensor
        /// </summary>
        /// <param name="timeValue"></param>
        /// <returns></returns>
        [HttpPost("timespan")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<TimeSpanSensorValue>> Post([FromBody] TimeSpanSensorValue timeValue) => GetAddDataResult(timeValue);


        /// <summary>
        /// Receives value of version sensor
        /// </summary>
        /// <param name="versionValue"></param>
        /// <returns></returns>
        [HttpPost("version")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<VersionSensor>> Post([FromBody] VersionSensor versionValue) => GetAddDataResult(versionValue);


        /// <summary>
        /// Receives value of rate sensor
        /// </summary>
        /// <param name="rateValue"></param>
        /// <returns></returns>
        [HttpPost("rate")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<RateSensorValue>> Post([FromBody] RateSensorValue rateValue) => GetAddDataResult(rateValue);


        /// <summary>
        /// Receives value of double bar sensor
        /// </summary>
        /// <param name="doubleBarValue"></param>
        /// <returns></returns>
        [HttpPost("doubleBar")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<DoubleBarSensorValue>> Post([FromBody] DoubleBarSensorValue doubleBarValue) => GetAddDataResult(doubleBarValue);


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
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<IntBarSensorValue>> Post([FromBody] IntBarSensorValue intBarValue) => GetAddDataResult(intBarValue);


        /// <summary>
        /// Receives the value of file sensor, where the file contents are presented as byte array.
        /// </summary>
        /// <param name="fileValue"></param>
        /// <returns></returns>
        [HttpPost("file")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<FileSensorValue>> Post([FromBody] FileSensorValue fileValue) => GetAddDataResult(fileValue);


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
        [SendDataKeyPermissionFilter]
        public async Task<ActionResult<List<SensorValueBase>>> Post([FromBody, ModelBinder(typeof(SensorValueModelBinder))] List<SensorValueBase> values)
        {
            try
            {
                var response = new Dictionary<string, string>(values.Count);

                foreach (var value in values.OrderBy(u => u.Time))
                {
                    var result = await TryBuildAndAddData(value);

                    if (!result.IsOk)
                        response[value.Path] = result.Error;
                }

                return Ok(response);
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
        [SendDataKeyPermissionFilter]
        public ActionResult<List<UnitedSensorValue>> Post([FromBody] List<UnitedSensorValue> values)
        {
            if (values == null || values.Count == 0)
                return BadRequest();

            try
            {
                _collector.WebRequestsSensors.Total.AddReceiveData(values.Count);

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
        [TypeFilter<ReadDataKeyPermissionFilter>]
        public async Task<ActionResult<string>> Get([FromBody] HistoryRequest request)
        {
            try
            {
                var coreRequest = await TryCheckReadHistoryRequest(request);

                if (coreRequest.IsOk)
                {
                    var historyValues = await _cache.GetSensorValues(coreRequest.Value).Flatten();
                    var response = JsonSerializer.Serialize(historyValues.Convert());

                    return Ok(response);
                }

                return StatusCode(406, coreRequest.Error);
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
        [TypeFilter<ReadDataKeyPermissionFilter>]
        public async Task<IActionResult> Get([FromBody] FileHistoryRequest request) //TODO merge with ExportHistory History controller
        {
            try
            {
                var coreRequest = await TryCheckReadHistoryRequest(request);

                if (coreRequest.IsOk)
                {
                    var historyValues = await _cache.GetSensorValues(coreRequest.Value).Flatten();
                    var response = historyValues.ConvertToCsv();

                    return request.IsZipArchive
                           ? File(response.CompressToZip(request.FileName, request.Extension), $"{request.FileName}.zip".GetContentType())
                           : File(Encoding.UTF8.GetBytes(response), $"{request.FileName}.{request.Extension}".GetContentType());
                }

                return StatusCode(406, coreRequest.Error);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to get history!");
                return BadRequest(request);
            }
        }


        /// <summary>
        /// Add new sensor with selected properties or update sensor meta info
        /// </summary>
        /// <param name="sensorUpdate"></param>
        /// <returns></returns>
        [HttpPost("addOrUpdate")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [TypeFilter<SendDataKeyPermissionFilter>]
        public async Task<ActionResult<AddOrUpdateSensorRequest>> Post([FromBody] AddOrUpdateSensorRequest sensorUpdate)
        {
            try
            {
                var result = await TryBuildAndApplySensorUpdateRequest(sensorUpdate);

                return result.IsOk ? Ok() : StatusCode(406, result.Error);
            }
            catch (Exception e)
            {
                var message = $"Failed to update sensor! Update request: {JsonSerializer.Serialize(sensorUpdate)}";

                _logger.LogError(e, message);

                return BadRequest(message);
            }
        }

        /// <summary>
        /// List of sensor commands
        /// </summary>
        /// <param name="sensorCommands"></param>
        /// <returns>Dictionary that contains commands error. Key is path to sensor, Value is error</returns>
        [HttpPost("commands")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [TypeFilter<SendDataKeyPermissionFilter>]
        public async Task<ActionResult<Dictionary<string, string>>> Post([FromBody, ModelBinder(typeof(SensorCommandModelBinder))] List<CommandRequestBase> sensorCommands)
        {
            var response = new Dictionary<string, string>(sensorCommands.Count);

            try
            {
                for (var i = 0; i < sensorCommands.Count; i++)
                {
                    var path = sensorCommands[i].Path;

                    if (sensorCommands[i] is AddOrUpdateSensorRequest sensorUpdate)
                    {
                        var result = await TryBuildAndApplySensorUpdateRequest(sensorUpdate);

                        if (!result.IsOk)
                            response[path] = result.Error;
                    }
                    else
                        response[path] = $"This type of command is not supported";
                }

                return Ok(response);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to update sensors!");
                return BadRequest(response);
            }
        }


        private bool CanAddToQueue(StoreInfo storeInfo, out string message)
        {
            if (storeInfo.TryCheckRequest(out message) && _cache.TryCheckKeyWritePermissions(storeInfo, out message))
            {
                _updatesQueue.AddItem(storeInfo);
                return true;
            }

            return false;
        }

        private async Task<ActionResult<T>> GetAddDataResult<T>(T value) where T : SensorValueBase
        {
            try
            {
                var result = await TryBuildAndAddData(value);

                return result.IsOk ? Ok(value) : StatusCode(406, result.Error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to put data!");

                return BadRequest(value);
            }
        }

        private async Task<TaskResult> TryBuildAndAddData<T>(T value)
            where T : SensorValueBase
        {
            try
            {
                var infoRequest = await IsValidPublicApiRequest(value);

                if (infoRequest.IsOk)
                {
                    var info = infoRequest.Value;

                    var storeInfo = new StoreInfo(info.Key.Id, value.Path)
                    {
                        BaseValue = value.Convert(),
                        Product = info.Product
                    };

                    var result = CanAddToQueue(storeInfo, out var error);

                    if (result)
                    {
                        _collector.WebRequestsSensors[info.TelemetryPath].AddReceiveData(1);
                        _collector.WebRequestsSensors.Total.AddReceiveData(1);
                    }

                    return result ? TaskResult.Ok : new TaskResult(error);
                }

            }
            catch (Exception ex)
            {

            }

            return _invalidRequestResult;
        }

        private async Task<TaskResult<HistoryRequestModel>> TryCheckReadHistoryRequest(HistoryRequest apiRequest)
        {
            var infoRequest = await IsValidPublicApiRequest(apiRequest);

            if (infoRequest.IsOk)
            {
                var coreRequest = apiRequest.Convert(infoRequest.Value.Key.Id);
                var isValid = apiRequest.TryValidate(out var error) && coreRequest.TryCheckRequest(out error);

                return isValid ? new TaskResult<HistoryRequestModel>(coreRequest) : TaskResult<HistoryRequestModel>.AsError(error);
            }

            return TaskResult<HistoryRequestModel>.AsError(InvalidRequest);
        }

        private async Task<TaskResult> TryBuildAndApplySensorUpdateRequest(AddOrUpdateSensorRequest apiRequest)
        {
            var infoRequest = await IsValidPublicApiRequest(apiRequest);

            if (infoRequest.IsOk)
            {
                var relatedPath = apiRequest.Path;
                var sensorType = apiRequest.SensorType;
                var info = infoRequest.Value;

                if (!_cache.TryGetSensorByPath(info.Product.DisplayName, relatedPath, out var sensor) && sensorType is null)
                    return new TaskResult($"{nameof(apiRequest.SensorType)} property is required, because sensor {relatedPath} doesn't exist");

                var coreRequest = new SensorAddOrUpdateRequestModel(info.Key.Id, relatedPath)
                {
                    Update = apiRequest.Convert(sensor?.Id ?? Guid.Empty, info.Key.DisplayName),
                    Type = sensorType?.Convert() ?? Core.Model.SensorType.Boolean,
                };

                return _cache.TryAddOrUpdateSensor(coreRequest, out var error) ? TaskResult.Ok : new TaskResult(error);
            }

            return _invalidRequestResult;
        }

        private async Task<TaskResult<PublicApiRequestInfo>> IsValidPublicApiRequest(BaseRequest request)
        {
            var context = HttpContext;

            if (context.TryGetPublicApiInfo(out var info))
                return new TaskResult<PublicApiRequestInfo>(info);

            if (TelemetryCollector.TryAddKeyToHeader(context, request.Key))
            {
                var result = await _telemetry.TryRegisterPublicApiRequest(HttpContext);

                if (result && context.TryGetPublicApiInfo(out info))
                    return new TaskResult<PublicApiRequestInfo>(info);
            }

            return TaskResult<PublicApiRequestInfo>.AsError("Cannot process a access key in the body");
        }
    }
}