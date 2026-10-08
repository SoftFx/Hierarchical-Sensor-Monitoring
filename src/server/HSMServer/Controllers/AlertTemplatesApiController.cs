using System;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// CRUD over alert templates (epic #1347). Bearer-token authenticated only (the
    /// HsmApiToken scheme — see the HsmApiToken security scheme of this document);
    /// served on the web-UI port only. The token mirrors its owner: reads follow the
    /// owner's sight, writes additionally need the owner's Manager role at the folder
    /// and a read-write token; invisible folders answer the same 404 as an unknown id.
    /// </summary>
    // REST CRUD over alert templates — the first /api/v1 resource controller (#1351,
    // epic #1347). The class attributes are exactly what ManagementApiGuardMiddleware
    // admits into the management area: [ManagementApi] + the HsmApiToken management
    // policy (bearer token only, SitePort only, plain 401 challenge) — this controller
    // never runs under the cookie scheme and derives from ControllerBase, not the
    // cookie-world BaseController.
    //
    // Authorization is per request through IApiTokenAuthorizationService, at the
    // template's FOLDER boundary: a token is a full mirror of its owner (#1384), so
    // reads follow the owner's sight and writes additionally require the owner's
    // Manager role at the boundary and a read-write token. The evaluator's
    // 403/404 split is preserved verbatim — an invisible folder is a
    // 404 so callers cannot enumerate templates, and authorization always precedes
    // body validation for the same reason. Every error is the area's uniform JSON
    // contract (#1353, ManagementApiErrors): writes that fail inside the cache (folder
    // without products, partial per-sensor policy removal) are 409, request-shape
    // problems are 400 with field-keyed details. The global exception handler renders
    // Razor HTML, so nothing is allowed to throw out of an action.
    //
    // Since #1393 the reads render AlertReadService; since the MCP write-tool
    // preparation the WRITE paths (authorization ordering, structural/semantic
    // validation, cache calls, the create-409 templateId disclosure) live in
    // AlertTemplateAdministrationService — extracted verbatim (#1393 precedent) so
    // the REST controller and the MCP tools share one decision. Thin on purpose:
    // routing + the uniform contract.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/alertTemplates")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AlertTemplatesApiController : ControllerBase
    {
        // Size guard rails the web UI gets from its widgets; the API states them
        // explicitly. They bound the collection COUNTS and the name length only —
        // individual strings (paths, message templates, target values) stay bounded
        // by the request body limit alone (see Known Issues in feature.md).
        // Canonical home: AlertTemplateAdministrationService (the write engine);
        // aliased here for the published surface's readers and the test pins.
        public const int MaxNameLength = AlertTemplateAdministrationService.MaxNameLength;
        public const int MaxPaths = AlertTemplateAdministrationService.MaxPaths;
        public const int MaxPolicies = AlertTemplateAdministrationService.MaxPolicies;

        private readonly AlertReadService _reader;
        private readonly AlertTemplateAdministrationService _writer;

        public AlertTemplatesApiController(AlertReadService reader, AlertTemplateAdministrationService writer)
        {
            _reader = reader;
            _writer = writer;
        }


        /// <summary>
        /// List templates, paginated, ordered by name then id. Only templates whose
        /// folder is visible to the token's owner are listed — everything else is
        /// silently absent, never a per-item 403.
        /// </summary>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet]
        [ProducesResponseType(typeof(ApiPageDto<AlertTemplateDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetTemplates(int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            Ok(_reader.ListTemplates(User, page, pageSize, HttpContext.RequestAborted));

        /// <summary>Get one template by id.</summary>
        /// <param name="id">Template id.</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(AlertTemplateDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetTemplate(Guid id) =>
            _reader.GetTemplate(id, User).ToActionResult();

        /// <summary>
        /// Create a template. A client-sent id is ignored (the server generates one);
        /// the response echoes the STORED template — normalized ids, chat display names.
        /// </summary>
        /// <remarks>Requires a read-write token and the owner's write access at the target folder; the folder must exist, be visible to the token's owner and contain products.</remarks>
        [HttpPost]
        [ProducesResponseType(typeof(AlertTemplateDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateTemplate([FromBody] AlertTemplateDto dto)
        {
            var result = await _writer.CreateTemplateAsync(User, dto);

            return result.Success
                ? CreatedAtAction(nameof(GetTemplate), new { id = result.Value.Id }, result.Value)
                : PolicyWriteResult.ToActionResult(result.Failure);
        }

        /// <summary>
        /// Update a template (upsert semantics). Moving it to another folder requires
        /// the owner's write access on BOTH folders; the response echoes the stored template.
        /// </summary>
        /// <param name="id">Template id; must equal the body id when the body carries one.</param>
        [HttpPut("{id:guid}")]
        [ProducesResponseType(typeof(AlertTemplateDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] AlertTemplateDto dto) =>
            (await _writer.UpdateTemplateAsync(id, User, dto)).ToActionResult();

        /// <summary>Delete a template and strip its per-sensor policies.</summary>
        /// <param name="id">Template id.</param>
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteTemplate(Guid id)
        {
            var result = await _writer.DeleteTemplateAsync(id, User);

            return result.Success ? NoContent() : PolicyWriteResult.ToActionResult(result.Failure);
        }
    }
}
