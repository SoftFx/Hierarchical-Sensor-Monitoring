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
        /// (folders and sensors of the node itself, no recursion). For the sensors
        /// of the whole subtree use the sensor search with product = this id.
        /// </summary>
        /// <param name="id">Node id (a root product or a nested folder).</param>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(NodeDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetNode(Guid id)
        {
            // Unknown id: the plain area 404, issued BEFORE the evaluator call —
            // the area rule that keeps unknown and invisible indistinguishable
            // without paying a security event for every random id.
            if (!_cache.TryGetProduct(id, out var node) || node is null)
                return ManagementApiErrors.NotFound();

            // Reads are never forbidden in the owner-mirror model (the read-only
            // flag constrains writes); the Forbidden arm is unreachable and kept
            // only so the evaluator's full decision surface maps to a response.
            var decision = _authorization.AuthorizeRead(User, ApiTokenResource.Product(id));

            return decision switch
            {
                ApiTokenAuthorization.Allowed => Ok(SensorTreeDtoMapper.ToNodeDto(node)),
                ApiTokenAuthorization.Forbidden => ManagementApiErrors.Forbidden(
                    "The token's owner cannot see this node."),
                _ => ManagementApiErrors.NotFound(),
            };
        }
    }
}
