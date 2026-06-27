using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using AngleSharp.Io;
using HSMCommon.Extensions;
using HSMCommon.TaskResult;
using HSMSensorDataObjects;
using HSMSensorDataObjects.HistoryRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.ApiObjectsConverters;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.Model.Requests;
using HSMServer.ServerConfiguration;
using HSMServer.Extensions;
using HSMServer.Middleware;
using HSMServer.Middleware.Telemetry;
using HSMServer.ModelBinders;
using HSMServer.ObsoleteUnitedSensorValue;
using HSMServer.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using HSMSensorDataObjects.SensorRequests;


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

        private static readonly TaskResult _invalidRequestResult = TaskResult.FromError(InvalidRequest);

        private const string DirectiveHeader = "X-Hsm-Directive";
        private const string AgentVersionHeader = "X-Agent-Version";
        private static readonly string[] _knownSensorGroups = ["computer", "system", "disk", "network", "module", "process"];

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly DataCollectorWrapper _collector;
        private readonly TelemetryCollector _telemetry;
        private readonly ITreeValuesCache _cache;
        private readonly IWebHostEnvironment _environment;
        private readonly IServerConfig _config;


        public SensorsController(DataCollectorWrapper dataCollector, ITreeValuesCache cache,
            TelemetryCollector telemetry, IWebHostEnvironment environment, IServerConfig config)
        {
            _telemetry = telemetry;
            _collector = dataCollector;
            _cache = cache;
            _environment = environment;
            _config = config;
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
        /// Receives value of enum sensor
        /// </summary>
        /// <param name="enumValue"></param>
        /// <returns></returns>
        [HttpPost("enum")]
        [Consumes(MediaTypeNames.Application.Json)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status406NotAcceptable)]
        [SendDataKeyPermissionFilter]
        public Task<ActionResult<EnumSensorValue>> Post([FromBody] EnumSensorValue enumValue) => GetAddDataResult(enumValue);


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
                if (values.Count == 0)
                    return BadRequest(values);

                var infoRequest = await IsValidPublicApiRequest(values.FirstOrDefault());

                if (infoRequest.IsOk)
                {
                    var info = infoRequest.Value;

                    var response = await _cache.AddSensorValuesAsync(info.Key.Id, info.Product.Id, values);

                    _collector.WebRequestsSensors[info.TelemetryPath].AddReceiveData(values.Count);
                    _collector.WebRequestsSensors.Total.AddReceiveData(values.Count);

                    // The agent sends data as batches to /list, so directives must ride this response
                    // too — not only the typed single-value endpoints. Without this the agent never
                    // sees update-available/sensor-* directives (its data path is /list).
                    EmitAgentDirectives();

                    return Ok(response);
                }

                return BadRequest(values);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to put data", ex);
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
            catch (Exception ex)
            {
                _logger.Error("Failed to get history!", ex);
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
            catch (Exception ex)
            {
                _logger.Error("Failed to get history!", ex);
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
            catch (Exception ex)
            {
                var message = $"Failed to update sensor! Update request: {JsonSerializer.Serialize(sensorUpdate)}";

                _logger.Error(message, ex);

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
            catch (Exception ex)
            {
                _logger.Error("Failed to update sensors!", ex);
                return BadRequest(response);
            }
        }


        private async Task<ActionResult<T>> GetAddDataResult<T>(T value) where T : SensorValueBase
        {
            try
            {
                var result = await TryBuildAndAddData(value);

                if (!result.IsOk)
                    return StatusCode(406, result.Error);

                EmitAgentDirectives();
                return Ok(value);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to put data!", ex);

                return BadRequest(value);
            }
        }

        private void EmitAgentDirectives()
        {
            // Phase 1: update-available — only when X-Agent-Version request header is present and
            // the server has a newer binary staged in wwwroot/agent/.
            // NOTE: agent/staged versions are plain numeric "x.y.z"; a semver pre-release/build suffix
            // would fail Version.TryParse and silently skip the directive (acceptable by contract).
            var agentVersionHeader = Request.Headers[AgentVersionHeader].ToString();
            if (_config.Agent.AutoUpdateEnabled
                && !string.IsNullOrEmpty(agentVersionHeader)
                && Version.TryParse(agentVersionHeader, out var agentVer)
                && !string.IsNullOrEmpty(_environment?.WebRootPath))
            {
                var staged = ReadStagedVersion(_environment.WebRootPath);
                if (Version.TryParse(staged, out var stagedVer) && stagedVer > agentVer)
                    Response.Headers.Append(DirectiveHeader, $"update-available:{staged}");
            }

            // Phase 2: sensor-disable / sensor-enable — driven by per-product DisabledSensorGroups.
            // Only for directive-aware agents (those that send X-Agent-Version); ordinary
            // HSMDataCollector clients ignore these, so don't append six headers to their every POST.
            if (string.IsNullOrEmpty(agentVersionHeader) || !HttpContext.TryGetPublicApiInfo(out var info))
                return;

            // Emit the FULL desired state (enable OR disable) for every known group, not just the
            // disabled ones — otherwise re-enabling a group in the UI never reaches the agent: the
            // server would simply stop sending the disable, leaving the agent's persisted config
            // stuck disabled. The agent no-ops a directive whose state is unchanged, so the extra
            // headers are cheap.
            foreach (var group in _knownSensorGroups)
            {
                var verb = info.Product.DisabledSensorGroups.Contains(group) ? "sensor-disable" : "sensor-enable";
                Response.Headers.Append(DirectiveHeader, $"{verb}:{group}");
            }
        }

        private static string _stagedVersionCache = "0.0.0";
        private static long _stagedVersionReadAtTicks; // DateTime.UtcNow.Ticks of last disk read; 0 = never
        private static readonly object _stagedVersionLock = new();
        private const long StagedVersionCacheTtlTicks = 15 * TimeSpan.TicksPerSecond;

        // wwwroot/agent/version.txt is consulted on every data POST (the /list batch endpoint is the
        // server's hottest path), but only changes when ops stage a new binary. Cache it with a short
        // TTL so the hot path hits memory instead of synchronous disk I/O, while still picking up a
        // newly staged version within the TTL window. Cache hits are lock-free.
        private static string ReadStagedVersion(string webRootPath)
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            if (nowTicks - System.Threading.Interlocked.Read(ref _stagedVersionReadAtTicks) < StagedVersionCacheTtlTicks)
                return _stagedVersionCache;

            lock (_stagedVersionLock)
            {
                if (nowTicks - _stagedVersionReadAtTicks < StagedVersionCacheTtlTicks)
                    return _stagedVersionCache; // another thread refreshed while we waited

                try
                {
                    var path = System.IO.Path.Combine(webRootPath, "agent", "version.txt");
                    var text = System.IO.File.ReadAllText(path).Trim();
                    _stagedVersionCache = string.IsNullOrEmpty(text) ? "0.0.0" : text;
                }
                catch
                {
                    _stagedVersionCache = "0.0.0";
                }

                System.Threading.Interlocked.Exchange(ref _stagedVersionReadAtTicks, nowTicks);
                return _stagedVersionCache;
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

                    var result = await _cache.AddSensorValueAsync(info.Key.Id, info.Product.Id, value);

                    if (result.IsOk)
                    {
                        _collector.WebRequestsSensors[info.TelemetryPath].AddReceiveData(1);
                        _collector.WebRequestsSensors.Total.AddReceiveData(1);
                    }

                    return result;
                }

            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            return _invalidRequestResult;
        }

        private async Task<TaskResult<HistoryRequestModel>> TryCheckReadHistoryRequest(HistoryRequest apiRequest)
        {
            var infoRequest = await IsValidPublicApiRequest(apiRequest);

            if (infoRequest.IsOk)
            {
                var coreRequest = apiRequest.Convert(infoRequest.Value.Key.Id, infoRequest.Value.Product.Id);
                var isValid = apiRequest.TryValidate(out var error) && coreRequest.TryCheckRequest(out error);

                return isValid ? TaskResult<HistoryRequestModel>.FromValue(coreRequest) : TaskResult<HistoryRequestModel>.FromError(error);
            }

            return TaskResult<HistoryRequestModel>.FromError(InvalidRequest);
        }

        private async Task<TaskResult> TryBuildAndApplySensorUpdateRequest(AddOrUpdateSensorRequest apiRequest)
        {
            var infoRequest = await IsValidPublicApiRequest(apiRequest);

            if (infoRequest.IsOk)
            {
                var relatedPath = apiRequest.Path;
                var sensorType = apiRequest.SensorType;
                var info = infoRequest.Value;

                var coreRequest = new SensorAddOrUpdateRequest(info.Product.Id, relatedPath)
                {
                    Update = apiRequest.Convert(Guid.Empty, info.Key.DisplayName),
                    Type = sensorType?.Convert() ?? HSMCommon.Model.SensorType.Boolean,
                };

                return await _cache.AddOrUpdateSensorAsync(coreRequest);
            }

            return _invalidRequestResult;
        }

        private async Task<TaskResult<PublicApiRequestInfo>> IsValidPublicApiRequest(BaseRequest request)
        {
            var context = HttpContext;

            if (context.TryGetPublicApiInfo(out var info))
                return TaskResult<PublicApiRequestInfo>.FromValue(info);

            if (TelemetryCollector.TryAddKeyToHeader(context, request.Key))
            {
                var result = await _telemetry.TryRegisterPublicApiRequest(HttpContext);

                if (result && context.TryGetPublicApiInfo(out info))
                    return TaskResult<PublicApiRequestInfo>.FromValue(info);
            }

            return TaskResult<PublicApiRequestInfo>.FromError("Cannot process a access key in the body");
        }
    }
}
