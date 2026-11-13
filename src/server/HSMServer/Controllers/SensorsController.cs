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
using HSMServer.Core.Cache;
using HSMServer.Core.Model.Requests;
using HSMServer.ServerConfiguration;
using HSMServer.Extensions;
using HSMServer.Middleware;
using HSMServer.Middleware.Telemetry;
using HSMServer.ModelBinders;
using HSMServer.SensorApi;
using HSMServer.SensorApi.Sessions;
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
        private const string InvalidRequest = SensorIngestionService.InvalidRequest;
        private const string KeyRequired = "An access key is required";

        private const string DirectiveHeader = "X-Hsm-Directive";
        private const string AgentVersionHeader = "X-Agent-Version";
        private static readonly string[] _knownSensorGroups = ["computer", "system", "disk", "network", "module", "process"];

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly SensorIngestionService _ingestion;
        private readonly CollectorSessionHandler _sessions;
        private readonly TelemetryCollector _telemetry;
        private readonly ITreeValuesCache _cache;
        private readonly IWebHostEnvironment _environment;
        private readonly IServerConfig _config;


        public SensorsController(SensorIngestionService ingestion, CollectorSessionHandler sessions, ITreeValuesCache cache,
            TelemetryCollector telemetry, IWebHostEnvironment environment, IServerConfig config)
        {
            _telemetry = telemetry;
            _ingestion = ingestion;
            _sessions = sessions;
            _cache = cache;
            _environment = environment;
            _config = config;
        }


        /// <summary>
        /// Response header that marks a <c>testConnection</c> answer as coming from an HSM server.
        /// The HSM Agent's retired-port check (#1655) moves an agent off 44330/44333 only when 443
        /// answers with it, so that a foreign service answering 200 on 443 of a host whose HSM is
        /// pinned to an old port (and merely down) cannot take its agents. A "not a stranger" check,
        /// not authentication: the agent accepts exactly the value "1" (kServerMarkerValue in
        /// src/agent/include/agent/retired_port.hpp).
        /// </summary>
        public const string ServerMarkerHeader = "X-Hsm-Server";

        [HttpGet("testConnection")]
        public ActionResult TestConnection()
        {
            Response.Headers[ServerMarkerHeader] = "1";
            return Ok();
        }


        /// <summary>
        /// The collector session (#1664): a WebSocket upgrade, subprotocol hsm.sensors.v1,
        /// authenticated by the same Key header as every other route here. Not an HTTP
        /// operation, so it stays out of the OpenAPI document; the contract is
        /// aicontext/features/api/wire-contract/feature.md. Two methods: over HTTP/1.1 the
        /// upgrade is a GET, over HTTP/2 it is a CONNECT.
        /// </summary>
        [AcceptVerbs("GET", "CONNECT", Route = "session")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public Task Session() => _sessions.HandleAsync(HttpContext);


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
                    var response = await _ingestion.AddValuesAsync(infoRequest.Value, values);

                    // The agent sends data as batches to /list, so directives must ride this response
                    // too — not only the typed single-value endpoints. Without this the agent never
                    // sees update-available/sensor-* directives (its data path is /list).
                    EmitAgentDirectives();

                    return Ok(response);
                }

                // The rejected-key reason is the 406 body the caller reads (#1649) —
                // the old BadRequest landed on a response the registration had
                // already started by hand and threw.
                return StatusCode(406, infoRequest.Error);
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
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

                // Exception first: Error(string, Exception) takes the exception for a
                // template argument and logs the message without it. Once per window for
                // the same fault of the same key, see the commands route below.
                if (_ingestion.IsFaultLogDue(CallerKeyId, ex, out var unlogged))
                    _logger.Error(ex, $"{message}{RepeatedFaultLog.Unlogged(unlogged)}");

                // The server's own fault, so 500 and not 400 (#1749): a collector takes a 4xx
                // on a registration as a final refusal and never sends it again, and repeats a
                // 5xx on its next cycle. The body is the one the 400 carried.
                return StatusCode(StatusCodes.Status500InternalServerError, message);
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
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [TypeFilter<SendDataKeyPermissionFilter>]
        public async Task<ActionResult<Dictionary<string, string>>> Post([FromBody, ModelBinder(typeof(SensorCommandModelBinder))] List<CommandRequestBase> sensorCommands)
        {
            // A request with no key at all, neither the Key header nor a key in any command,
            // is the request's fault: the access error every other key problem gets, and
            // nothing is applied (#1756). It used to end as a null reference inside the
            // loop, which is a 500 since #1749.
            if (sensorCommands.Count > 0 && sensorCommands.TrueForAll(HasNoKey))
                return StatusCode(StatusCodes.Status406NotAcceptable, KeyRequired);

            var response = new Dictionary<string, string>(sensorCommands.Count);

            Exception firstFault = null;
            var faulted = 0;

            // The callers are resolved for the whole batch first: the sensor moves its
            // registrations ask for (#1724) are made before any command is applied.
            var accepted = new List<(PublicApiRequestInfo Info, CommandRequestBase Command)>(sensorCommands.Count);

            for (var i = 0; i < sensorCommands.Count; i++)
            {
                try
                {
                    // Only a registration reads the caller; any other command is refused
                    // without it, exactly as before the logic moved to the service.
                    PublicApiRequestInfo info = null;
                    if (sensorCommands[i] is AddOrUpdateSensorRequest)
                    {
                        var infoRequest = await IsValidRegistrationRequest(sensorCommands[i]);

                        if (!infoRequest.IsOk)
                        {
                            // The rejection reason (unknown/expired/blocked key) is the
                            // per-path error the caller reads, not the generic
                            // invalid-request text (#1649).
                            response[sensorCommands[i].Path] = infoRequest.Error;

                            continue;
                        }

                        info = infoRequest.Value;
                    }

                    accepted.Add((info, sensorCommands[i]));
                }
                catch (Exception ex)
                {
                    // A fault of one command, see the loop below.
                    firstFault ??= ex;
                    faulted++;
                }
            }

            await _ingestion.ApplyPreviousPathsAsync(accepted);

            foreach (var (info, command) in accepted)
            {
                try
                {
                    var error = await _ingestion.ApplyCommandAsync(info, command, previousPathsApplied: true);

                    if (error is not null)
                        response[command.Path] = error;
                }
                catch (Exception ex)
                {
                    // A fault on one command does not end the batch (#1749). The answer
                    // will be a 500, on which a collector sends the whole package again
                    // every cycle, ahead of everything it issues later: were the loop to
                    // stop here, a command that faults on every repeat would keep all the
                    // commands after it from ever being applied. The rest is applied now,
                    // and applied once more on the resend, which is safe: a registration
                    // applied again is what every collector restart does.
                    firstFault ??= ex;
                    faulted++;
                }
            }

            if (firstFault is null)
                return Ok(response);

            // One line for the request, not one per command: during a storage outage every
            // command of a large package faults. And not one per cycle either: a collector
            // sends a package answered 500 again every cycle for as long as the fault
            // lasts, so the same fault of the same key is logged once per window, with the
            // number of requests that went unlogged since. Exception first, see addOrUpdate
            // above.
            if (_ingestion.IsFaultLogDue(CallerKeyId, firstFault, out var unlogged))
                _logger.Error(firstFault, $"Failed to update sensors: {faulted} of {sensorCommands.Count} command(s) faulted, the others were applied.{RepeatedFaultLog.Unlogged(unlogged)} The first fault:");

            // The server's own fault, so 500 and not 400 (#1749): a collector drops the
            // whole registration package on a 4xx and repeats it on a 5xx. A command the
            // server refuses is not an exception: it is an entry of the dictionary in a
            // 200. The body is the one the 400 carried: the per-path errors of the commands
            // the server refused. A faulted command has no entry in it.
            return StatusCode(StatusCodes.Status500InternalServerError, response);
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

                if (!infoRequest.IsOk)
                {
                    // The rejection reason (unknown/expired/blocked key) is the 406
                    // body the caller reads, not the generic invalid-request text (#1649).
                    return TaskResult.FromError(infoRequest.Error);
                }

                return await _ingestion.AddValueAsync(infoRequest.Value, value);
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            return TaskResult.FromError(InvalidRequest);
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

            return TaskResult<HistoryRequestModel>.FromError(infoRequest.Error);
        }

        private async Task<TaskResult> TryBuildAndApplySensorUpdateRequest(AddOrUpdateSensorRequest apiRequest)
        {
            var infoRequest = await IsValidRegistrationRequest(apiRequest);

            if (!infoRequest.IsOk)
                return TaskResult.FromError(infoRequest.Error);

            return await _ingestion.AddOrUpdateSensorAsync(infoRequest.Value, apiRequest);
        }

        // The key the request came with; empty when it could not be resolved.
        private Guid CallerKeyId => HttpContext.TryGetPublicApiInfo(out var caller) ? caller.Key.Id : Guid.Empty;

        private bool HasNoKey(BaseRequest request) => request.Key is null && !HttpContext.TryGetPublicApiInfo(out _);

        // Who sends a registration. The value routes resolve their caller with
        // IsValidPublicApiRequest alone and are not touched by the two cases handled here
        // (#1756), both of which matter for a registration only: a collector takes a 4xx on
        // it as final and sends a 5xx again.
        //   - No key at all is the request's fault: the access error, where the lookup
        //     below would fail on the missing key with a null reference, a 500.
        //   - A key that could not be looked up because the server itself failed is not a
        //     refused key: it leaves as an exception, which both routes answer 500.
        private async Task<TaskResult<PublicApiRequestInfo>> IsValidRegistrationRequest(BaseRequest request)
        {
            if (HasNoKey(request))
                return TaskResult<PublicApiRequestInfo>.FromError(KeyRequired);

            // The mark belongs to one lookup, and a batch makes one per command: cleared
            // first, so that a lookup that failed for an earlier command does not turn the
            // refusal of this one into a fault.
            HttpContext.ClearPublicApiFault();

            var infoRequest = await IsValidPublicApiRequest(request);

            if (!infoRequest.IsOk && HttpContext.HasPublicApiFault())
                throw new InvalidOperationException(infoRequest.Error);

            return infoRequest;
        }

        private Task<TaskResult<PublicApiRequestInfo>> IsValidPublicApiRequest(BaseRequest request)
        {
            var context = HttpContext;

            if (context.TryGetPublicApiInfo(out var info))
                return Task.FromResult(TaskResult<PublicApiRequestInfo>.FromValue(info));

            if (TelemetryCollector.TryAddKeyToHeader(context, request.Key))
            {
                // The rejected-key reason rides the ACTION's result (the 406
                // the caller sees) — writing the body by hand here started
                // the response, and MVC's formatting of the action's own
                // result then threw over it: three ERROR entries and a
                // truncated body per rejected request (#1649).
                if (_telemetry.TryRegisterPublicApiRequest(context, out var error))
                {
                    if (context.TryGetPublicApiInfo(out info))
                        return Task.FromResult(TaskResult<PublicApiRequestInfo>.FromValue(info));
                }
                else
                    return Task.FromResult(TaskResult<PublicApiRequestInfo>.FromError(error));
            }

            return Task.FromResult(TaskResult<PublicApiRequestInfo>.FromError("Cannot process a access key in the body"));
        }
    }
}
