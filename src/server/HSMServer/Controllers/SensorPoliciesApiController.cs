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
    /// Alert administration on a sensor (#1500): item-level CRUD over the
    /// sensor's data-alert policies and TTL policies. Bearer-token authenticated
    /// only (the HsmApiToken scheme); served on the web-UI port only. Writes need
    /// a read-write token and the owner's write access at the sensor; a read-only
    /// token never passes the management policy's method backstop (403 before the
    /// action runs) and the evaluator's write arm answers 403 for a Viewer owner.
    /// </summary>
    // Thin on purpose: reads render AlertReadService (the #1393 single visibility
    // source shared with MCP), writes render PolicyAdministrationService — the
    // controllers own routing, the uniform error contract and nothing else. The
    // write engine reads the current policy list, merges the ONE requested item
    // and sends a single atomic full-list SensorUpdate (the UI's own machinery,
    // last-write-wins per request).
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/sensors/{sensorId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class SensorPoliciesApiController : ControllerBase
    {
        private readonly AlertReadService _reader;
        private readonly PolicyAdministrationService _writer;

        public SensorPoliciesApiController(AlertReadService reader, PolicyAdministrationService writer)
        {
            _reader = reader;
            _writer = writer;
        }


        /// <summary>
        /// The sensor's data policies, ordered by policy id. Every policy is
        /// listed — template-owned ones included, with their template linkage.
        /// </summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet("policies")]
        [ProducesResponseType(typeof(ApiPageDto<PolicyDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetPolicies(Guid sensorId, int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            _reader.ListSensorPolicies(User, sensorId, page, pageSize).ToActionResult();

        /// <summary>One data policy of the sensor by id.</summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="policyId">Policy id.</param>
        [HttpGet("policies/{policyId:guid}")]
        [ProducesResponseType(typeof(PolicyDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetPolicy(Guid sensorId, Guid policyId) =>
            _reader.GetSensorPolicy(User, sensorId, policyId).ToActionResult();

        /// <summary>
        /// Create one data policy; the id is server-generated and every other
        /// policy of the sensor rides through untouched. The body fully specifies
        /// the policy; a condition property the sensor's type does not offer
        /// answers 422, as does a chat not available on the node or an unknown
        /// schedule reference.
        /// </summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="dto">The policy content.</param>
        [HttpPost("policies")]
        [ProducesResponseType(typeof(PolicyDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreatePolicy(Guid sensorId, [FromBody] PolicyDto dto)
        {
            var result = await _writer.CreateSensorPolicyAsync(sensorId, dto, User);

            return result.Success
                ? CreatedAtAction(nameof(GetPolicy), new { sensorId, policyId = result.Value.Id }, result.Value)
                : PolicyWriteResult.ToActionResult(result.Failure);
        }

        /// <summary>
        /// Replace the full content of one data policy (the route id wins). A
        /// template-owned policy accepts only the disable toggle — anything else
        /// answers 409.
        /// </summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="policyId">Policy id.</param>
        /// <param name="dto">The policy content.</param>
        [HttpPatch("policies/{policyId:guid}")]
        [ProducesResponseType(typeof(PolicyDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdatePolicy(Guid sensorId, Guid policyId, [FromBody] PolicyDto dto) =>
            (await _writer.UpdateSensorPolicyAsync(sensorId, policyId, dto, User)).ToActionResult();

        /// <summary>Remove one data policy; every other policy of the sensor rides through untouched.</summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="policyId">Policy id.</param>
        [HttpDelete("policies/{policyId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeletePolicy(Guid sensorId, Guid policyId)
        {
            var result = await _writer.DeleteSensorPolicyAsync(sensorId, policyId, User);

            return result.Success ? NoContent() : PolicyWriteResult.ToActionResult(result.Failure);
        }

        /// <summary>The sensor's TTL (inactivity) policies, ordered by policy id.</summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet("ttl-policies")]
        [ProducesResponseType(typeof(ApiPageDto<TtlPolicyDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetTtlPolicies(Guid sensorId, int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            _reader.ListSensorTtlPolicies(User, sensorId, page, pageSize).ToActionResult();

        /// <summary>One TTL policy of the sensor by id.</summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="policyId">Policy id.</param>
        [HttpGet("ttl-policies/{policyId:guid}")]
        [ProducesResponseType(typeof(TtlPolicyDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetTtlPolicy(Guid sensorId, Guid policyId) =>
            _reader.GetSensorTtlPolicy(User, sensorId, policyId).ToActionResult();

        /// <summary>
        /// Create one TTL policy. The interval is explicit — <c>interval</c>
        /// (a TimeSpan string) or <c>inherit</c>=true, never a null: the server
        /// owns the reset-to-parent translation.
        /// </summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="dto">The policy content.</param>
        [HttpPost("ttl-policies")]
        [ProducesResponseType(typeof(TtlPolicyDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateTtlPolicy(Guid sensorId, [FromBody] TtlPolicyDto dto)
        {
            var result = await _writer.CreateSensorTtlPolicyAsync(sensorId, dto, User);

            return result.Success
                ? CreatedAtAction(nameof(GetTtlPolicy), new { sensorId, policyId = result.Value.Id }, result.Value)
                : PolicyWriteResult.ToActionResult(result.Failure);
        }

        /// <summary>Replace one TTL policy; the interval/inherit switch is part of the content.</summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="policyId">Policy id.</param>
        /// <param name="dto">The policy content.</param>
        [HttpPatch("ttl-policies/{policyId:guid}")]
        [ProducesResponseType(typeof(TtlPolicyDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateTtlPolicy(Guid sensorId, Guid policyId, [FromBody] TtlPolicyDto dto) =>
            (await _writer.UpdateSensorTtlPolicyAsync(sensorId, policyId, dto, User)).ToActionResult();

        /// <summary>Remove one TTL policy; every other TTL policy of the sensor rides through untouched.</summary>
        /// <param name="sensorId">Sensor id.</param>
        /// <param name="policyId">Policy id.</param>
        [HttpDelete("ttl-policies/{policyId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteTtlPolicy(Guid sensorId, Guid policyId)
        {
            var result = await _writer.DeleteSensorTtlPolicyAsync(sensorId, policyId, User);

            return result.Success ? NoContent() : PolicyWriteResult.ToActionResult(result.Failure);
        }
    }
}
