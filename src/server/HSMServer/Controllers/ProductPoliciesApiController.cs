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
    /// Alert administration on a product (#1500): the product's own TTL policies
    /// (full CRUD) and the product's data-policy aggregate (the subtree's sensor
    /// policies — read, and writes routed to the owning sensor; a product owns no
    /// data policies of its own, per-node alert creation was removed in #1142, so
    /// POST /policies answers 422 pointing at the sensor endpoint). Bearer-token
    /// authenticated only (the HsmApiToken scheme); writes need a read-write token
    /// and the owner's write access.
    /// </summary>
    // The same thin-controller shape as SensorPoliciesApiController: reads render
    // AlertReadService, writes render PolicyAdministrationService, and the
    // product data-policy writes are applied atomically on the OWNING sensor's
    // full list (the product id only addresses the aggregate).
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/products/{productId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class ProductPoliciesApiController : ControllerBase
    {
        private readonly AlertReadService _reader;
        private readonly PolicyAdministrationService _writer;

        public ProductPoliciesApiController(AlertReadService reader, PolicyAdministrationService writer)
        {
            _reader = reader;
            _writer = writer;
        }


        /// <summary>
        /// The product's data-policy aggregate: every data policy of every sensor
        /// in the product's subtree, ordered by sensor path then policy id.
        /// </summary>
        /// <param name="productId">Product id.</param>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet("policies")]
        [ProducesResponseType(typeof(ApiPageDto<ProductPolicyDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetPolicies(Guid productId, int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            _reader.ListProductPolicies(User, productId, page, pageSize).ToActionResult();

        /// <summary>One data policy of the product's subtree by id, with its owning sensor.</summary>
        /// <param name="productId">Product id.</param>
        /// <param name="policyId">Policy id.</param>
        [HttpGet("policies/{policyId:guid}")]
        [ProducesResponseType(typeof(ProductPolicyDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetPolicy(Guid productId, Guid policyId) =>
            _reader.GetProductPolicy(User, productId, policyId).ToActionResult();

        /// <summary>
        /// Not expressible: a product owns no data policies (the aggregate is
        /// read-addressed and writes route to the owning sensor). Always answers
        /// 422 with the sensor endpoint to use instead.
        /// </summary>
        /// <param name="productId">Product id.</param>
        /// <param name="dto">The policy content (never applied).</param>
        [HttpPost("policies")]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreatePolicy(Guid productId, [FromBody] PolicyDto dto) =>
            PolicyWriteResult.ToActionResult(
                (await _writer.CreateProductPolicyAsync(productId, dto, User)).Failure);

        /// <summary>
        /// Replace the full content of one data policy found anywhere in the
        /// product's subtree; the write is applied atomically on the OWNING
        /// sensor's full list. A template-owned policy accepts only the disable
        /// toggle — anything else answers 409.
        /// </summary>
        /// <param name="productId">Product id.</param>
        /// <param name="policyId">Policy id.</param>
        /// <param name="dto">The policy content.</param>
        [HttpPatch("policies/{policyId:guid}")]
        [ProducesResponseType(typeof(ProductPolicyDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdatePolicy(Guid productId, Guid policyId, [FromBody] PolicyDto dto) =>
            (await _writer.UpdateProductPolicyAsync(productId, policyId, dto, User)).ToActionResult();

        /// <summary>Remove one data policy of the product's subtree, through the owning sensor's full list.</summary>
        /// <param name="productId">Product id.</param>
        /// <param name="policyId">Policy id.</param>
        [HttpDelete("policies/{policyId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeletePolicy(Guid productId, Guid policyId)
        {
            var result = await _writer.DeleteProductPolicyAsync(productId, policyId, User);

            return result.Success ? NoContent() : PolicyWriteResult.ToActionResult(result.Failure);
        }

        /// <summary>The product's own TTL policies, ordered by policy id.</summary>
        /// <param name="productId">Product id.</param>
        /// <param name="page">1-based page number; clamped into [1, totalPages].</param>
        /// <param name="pageSize">Page size, 1..200 (default 50).</param>
        [HttpGet("ttl-policies")]
        [ProducesResponseType(typeof(ApiPageDto<TtlPolicyDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetTtlPolicies(Guid productId, int page = 1, int pageSize = ApiPagination.DefaultPageSize) =>
            _reader.ListProductTtlPolicies(User, productId, page, pageSize).ToActionResult();

        /// <summary>One TTL policy of the product by id.</summary>
        /// <param name="productId">Product id.</param>
        /// <param name="policyId">Policy id.</param>
        [HttpGet("ttl-policies/{policyId:guid}")]
        [ProducesResponseType(typeof(TtlPolicyDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public IActionResult GetTtlPolicy(Guid productId, Guid policyId) =>
            _reader.GetProductTtlPolicy(User, productId, policyId).ToActionResult();

        /// <summary>
        /// Create one TTL policy on the product itself. On a product,
        /// <c>inherit</c>=true resolves against the product's OWN Inactivity
        /// Period setting (bounded — no parent-chain climb).
        /// </summary>
        /// <param name="productId">Product id.</param>
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
        public async Task<IActionResult> CreateTtlPolicy(Guid productId, [FromBody] TtlPolicyDto dto)
        {
            var result = await _writer.CreateProductTtlPolicyAsync(productId, dto, User);

            return result.Success
                ? CreatedAtAction(nameof(GetTtlPolicy), new { productId, policyId = result.Value.Id }, result.Value)
                : PolicyWriteResult.ToActionResult(result.Failure);
        }

        /// <summary>Replace one TTL policy of the product; the interval/inherit switch is part of the content.</summary>
        /// <param name="productId">Product id.</param>
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
        public async Task<IActionResult> UpdateTtlPolicy(Guid productId, Guid policyId, [FromBody] TtlPolicyDto dto) =>
            (await _writer.UpdateProductTtlPolicyAsync(productId, policyId, dto, User)).ToActionResult();

        /// <summary>Remove one TTL policy of the product; every other TTL policy rides through untouched.</summary>
        /// <param name="productId">Product id.</param>
        /// <param name="policyId">Policy id.</param>
        [HttpDelete("ttl-policies/{policyId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ManagementApiErrorDto), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteTtlPolicy(Guid productId, Guid policyId)
        {
            var result = await _writer.DeleteProductTtlPolicyAsync(productId, policyId, User);

            return result.Success ? NoContent() : PolicyWriteResult.ToActionResult(result.Failure);
        }
    }
}
