using System;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Sensor muting of the management API (#1569): suppress a sensor's alert
    /// notifications, indefinitely or until a chosen instant, and lift the mute
    /// again — the REST twin of the web UI's ignore-notifications modal. Bearer-
    /// token authenticated only (the HsmApiToken scheme); served on the web-UI
    /// port only. Writes need a read-write token and the owner's write access at
    /// the sensor; unknown and invisible ids answer the SAME 404. A Blocked
    /// sensor is not mutable and answers 409.
    /// </summary>
    // Thin on purpose: both actions render SensorMuteAdministrationService —
    // the controllers own routing, the uniform error contract and nothing else.
    // The mute engine itself stays in the core (UpdateMutedSensorStateAsync,
    // the same entry the web UI drives), including the system-initiated
    // auto-unmute at period expiry; the response echoes the resulting sensor
    // (state + endOfMuting) so a mute is observable without a second request.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/sensors/{sensorId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class SensorMuteApiController : ControllerBase
    {
        private readonly SensorMuteAdministrationService _writer;

        public SensorMuteApiController(SensorMuteAdministrationService writer)
        {
            _writer = writer;
        }


        /// <summary>
        /// Mute the sensor's alert notifications. The body is OPTIONAL: omitted
        /// (or a null <c>endOfMuting</c>) mutes indefinitely; an instant (UTC
        /// ISO 8601, must be in the future) mutes until it — the core's
        /// system-initiated sweep auto-unmutes at expiry. The response echoes
        /// the resulting sensor.
        /// </summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="dto">Optional muting period.</param>
        [HttpPost("mute")]
        [ProducesResponseType(typeof(SensorDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> MuteSensor(Guid sensorId, [FromBody] SensorMuteRequestDto dto = null) =>
            (await _writer.MuteSensorAsync(sensorId, dto?.EndOfMuting, User)).ToActionResult();

        /// <summary>
        /// Unmute the sensor: notifications resume and the stored muting period
        /// clears. The response echoes the resulting sensor. A sensor that is
        /// not muted answers 200 with its unchanged state (idempotent).
        /// </summary>
        /// <param name="sensorId">Sensor id.</param>
        [HttpDelete("mute")]
        [ProducesResponseType(typeof(SensorDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UnmuteSensor(Guid sensorId) =>
            (await _writer.UnmuteSensorAsync(sensorId, User)).ToActionResult();
    }
}
