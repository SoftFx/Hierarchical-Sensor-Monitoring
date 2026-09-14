using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
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
    // MCP tools; this controller is the REST rendering of it.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/products")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class ProductsApiController : ControllerBase
    {
        private readonly SensorTreeReadService _reader;

        public ProductsApiController(SensorTreeReadService reader)
        {
            _reader = reader;
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
            Ok(_reader.ListProducts(User, page, pageSize));
    }
}
