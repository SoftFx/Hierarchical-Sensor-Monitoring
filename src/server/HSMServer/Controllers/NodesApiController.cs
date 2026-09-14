using System;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// One tree node — a root product or a nested folder — with its direct children
    /// (#1386). Bearer-token authenticated only (the HsmApiToken scheme); served on
    /// the web-UI port only. Unknown and invisible ids answer the SAME 404 (the
    /// area's anti-enumeration rule).
    /// </summary>
    // Products and folders are one model (ProductModel in the flat _tree index);
    // the evaluator authorizes both through the Product boundary. Sensors are NOT
    // addressable here — their node body is the sensor endpoint itself; a sensor id
    // in this route is an unknown node (plain 404).
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/nodes")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class NodesApiController : ControllerBase
    {
        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;

        public NodesApiController(ITreeValuesCache cache, IApiTokenAuthorizationService authorization)
        {
            _cache = cache;
            _authorization = authorization;
        }


        /// <summary>
        /// Get a node (product or folder) by id: metadata plus its DIRECT children
        /// (no recursion). The folders list is paginated (foldersPage/foldersPageSize,
        /// name-ordered); the sensors list is capped at 200 with totalSensors
        /// carrying the uncapped count — the paginated sensor search with
        /// product = this id serves the full sensor list. Folder ids beyond the
        /// first page are reachable ONLY here: walk the pages.
        /// </summary>
        /// <param name="id">Node id (a root product or a nested folder).</param>
        /// <param name="foldersPage">1-based page of the folders list; clamped into [1, totalPages].</param>
        /// <param name="foldersPageSize">Folders page size, 1..200 (default 200 — the single-request ceiling).</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(NodeDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetNode(Guid id, int foldersPage = 1, int foldersPageSize = SensorTreeDtoMapper.MaxChildrenPerNode)
        {
            // Unknown id: the plain area 404, issued BEFORE the evaluator call —
            // the area rule that keeps unknown and invisible indistinguishable
            // without paying a security event for every random id.
            if (!_cache.TryGetProduct(id, out var node) || node is null)
                return ManagementApiErrors.NotFound();

            // Sight is keyed on the node's ROOT product (ProductsRoles holds root
            // ids; folder roles materialize per root) — authorizing the folder id
            // itself would 404 a scoped owner whose sensors under that folder ARE
            // listable via the root. Reads are never forbidden in the owner-mirror
            // model (the read-only flag constrains writes); the Forbidden arm is
            // unreachable and kept only so the evaluator's full decision surface
            // maps to a response.
            var decision = _authorization.AuthorizeRead(User, ApiTokenResource.Product(node.Root.Id));

            // The folders list is the ONLY addressable surface for direct
            // subfolders (the sensor search pages sensors, not folders), so it
            // paginates like every area list — a node with more than 200 direct
            // subfolders would otherwise strand folders 201..N unreachably
            // (#1387 review, round 3). Same clamps as the area convention.
            foldersPage = Math.Max(foldersPage, 1);
            foldersPageSize = Math.Min(foldersPageSize <= 0 ? SensorTreeDtoMapper.MaxChildrenPerNode : foldersPageSize,
                SensorTreeDtoMapper.MaxChildrenPerNode);

            var totalPages = node.SubProducts.Count == 0
                ? 0
                : (int)Math.Ceiling(node.SubProducts.Count / (double)foldersPageSize);

            foldersPage = Math.Min(foldersPage, Math.Max(totalPages, 1));

            return decision switch
            {
                ApiTokenAuthorization.Allowed => Ok(SensorTreeDtoMapper.ToNodeDto(node, foldersPage, foldersPageSize)),
                ApiTokenAuthorization.Forbidden => ManagementApiErrors.Forbidden(
                    "The token's owner cannot see this node."),
                _ => ManagementApiErrors.NotFound(),
            };
        }
    }
}
