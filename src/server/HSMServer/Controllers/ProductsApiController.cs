using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Root products visible to the token's owner (#1386) — the discovery entry
    /// point of the sensor-tree read surface for AI agents. Bearer-token
    /// authenticated only (the HsmApiToken scheme); served on the web-UI port only.
    /// An owner who sees nothing gets an empty list, not a 403: the list carries no
    /// per-item secret, and emptiness discloses nothing.
    /// </summary>
    // The first read surface over the live sensor tree (epic #1347 continuation).
    // Area conventions are identical to AlertSchedulesApiController (see
    // aicontext/features/server/management-api/): paginated ApiPageDto envelope,
    // name-then-id ordering, per-item visibility through the evaluator's IsVisible
    // (the owner-sight predicate — never a 403-per-item).
    //
    // Since #1391 the read logic lives in SensorTreeReadService, shared with the
    // MCP tools; this controller is the REST rendering of it. Since #1786 the
    // create path renders ProductAdministrationService the same way — the
    // token-side twin of the web UI's add-product modal (any live read-write
    // caller; the creator becomes the product's author).
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/products")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class ProductsApiController : ControllerBase
    {
        private readonly SensorTreeReadService _reader;
        private readonly ProductAdministrationService _writer;

        public ProductsApiController(SensorTreeReadService reader, ProductAdministrationService writer)
        {
            _reader = reader;
            _writer = writer;
        }


        /// <summary>
        /// List root products visible to the token's owner, paginated, ordered by
        /// name then id. Nested folders are not listed here — walk them via the node
        /// endpoint or search sensors with the product filter.
        /// </summary>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet]
        [ProducesResponseType(typeof(ApiPageDto<ProductDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetProducts(int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            Ok(_reader.ListProducts(User, page, pageSize, HttpContext.RequestAborted));

        /// <summary>
        /// Create a root product — the first step of the agent-installation
        /// scenario. Any live read-write token may create one (the web UI's rule:
        /// the creator becomes the product's author). The name follows the web
        /// form's rules (required, at most 100 chars of the allowed charset,
        /// unique among root products); the product's default access key and the
        /// tree-CRUD journal record are created by the same core path the web UI
        /// drives.
        /// </summary>
        /// <param name="request">The body carrying the new product's name.</param>
        [HttpPost]
        [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequestDto request)
        {
            var result = await _writer.CreateProductAsync(request?.Name, User);

            if (!result.Success)
                return PolicyWriteResult.ToActionResult(result.Failure);

            // A product has no GET {id} of its own — the node endpoint serves
            // the created tree node, so the Location header points there.
            return CreatedAtAction(nameof(NodesApiController.GetNode), "Nodes",
                new { id = result.Value.Id }, result.Value);
        }
    }
}
