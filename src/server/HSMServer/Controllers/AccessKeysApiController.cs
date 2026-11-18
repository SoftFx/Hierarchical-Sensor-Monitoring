using System;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Model.ManagementApi.AccessKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    /// <summary>
    /// A product's access keys (#1787) — the id-discovery surface of the
    /// agent-installation scenario: the listed key ids are the handles the
    /// write surface (#1789) takes, and each item's lastUseTime/lastIp answer
    /// "has the installed agent connected". Bearer-token authenticated only;
    /// reads follow the owner's product sight (unknown and invisible products
    /// answer the same 404; a key of another product is the same 404 too —
    /// the key id space is global).
    /// </summary>
    // The nested-resource shape of the policies controllers: the route
    // carries the parent product id, the controller is a thin REST rendering
    // of AccessKeysReadService (shared with the MCP list tool, the #1393
    // pattern).
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/products/{productId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AccessKeysApiController : ControllerBase
    {
        private readonly AccessKeysReadService _reader;

        public AccessKeysApiController(AccessKeysReadService reader)
        {
            _reader = reader;
        }


        /// <summary>
        /// The product's access keys, ordered by display name then id, paginated.
        /// The key id IS the credential and is returned deliberately (web parity —
        /// the web UI shows it to users who can see the product's keys).
        /// </summary>
        /// <param name="productId">Product id (any visible node of the tree works — sight is resolved at the node's root product).</param>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet("accessKeys")]
        [ProducesResponseType(typeof(ApiPageDto<AccessKeyDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetProductAccessKeys(Guid productId,
            int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            _reader.ListProductAccessKeys(User, productId, page, pageSize).ToActionResult();

        /// <summary>
        /// One access key of the product by id. An unknown key id, a key of a
        /// different product and a server-wide master key all answer the same 404.
        /// </summary>
        /// <param name="productId">Product id (any visible node of the tree works — sight is resolved at the node's root product).</param>
        /// <param name="keyId">Key id.</param>
        [HttpGet("accessKeys/{keyId:guid}")]
        [ProducesResponseType(typeof(AccessKeyDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetProductAccessKey(Guid productId, Guid keyId) =>
            _reader.GetProductAccessKey(User, productId, keyId).ToActionResult();
    }
}
