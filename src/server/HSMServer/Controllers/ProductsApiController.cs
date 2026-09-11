using System;
using System.Linq;
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
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/products")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class ProductsApiController : ControllerBase
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;

        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;

        public ProductsApiController(ITreeValuesCache cache, IApiTokenAuthorizationService authorization)
        {
            _cache = cache;
            _authorization = authorization;
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
        public IActionResult GetProducts(int page = 1, int pageSize = DefaultPageSize)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Min(pageSize <= 0 ? DefaultPageSize : pageSize, MaxPageSize);

            // _tree is a flat index of every product (roots and nested folders);
            // roots are the ones without a parent. Visibility follows the owner's
            // sight at the product boundary — the same predicate every sensor
            // listing resolves through.
            var all = _cache.GetProducts()
                .Where(product => product.IsRoot && _authorization.IsVisible(User, ApiTokenResource.Product(product.Id)))
                .OrderBy(product => product.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(product => product.Id)
                .ToList();

            var totalPages = all.Count == 0 ? 0 : (int)Math.Ceiling(all.Count / (double)pageSize);

            // Clamp the page into [1, totalPages]: unchecked (page - 1) * pageSize
            // would overflow int for huge page numbers, and a NEGATIVE Skip count
            // silently returns the FIRST page labeled as page N.
            page = Math.Min(page, Math.Max(totalPages, 1));

            return Ok(new ApiPageDto<ProductDto>
            {
                Items = [.. all.Skip((page - 1) * pageSize).Take(pageSize).Select(SensorTreeDtoMapper.ToProductDto)],
                Page = page,
                PageSize = pageSize,
                TotalCount = all.Count,
                TotalPages = totalPages,
            });
        }
    }
}
