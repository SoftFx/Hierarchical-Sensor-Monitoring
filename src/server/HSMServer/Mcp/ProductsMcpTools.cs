using System.ComponentModel;
using System.Security.Claims;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace HSMServer.Mcp
{
    /// <summary>
    /// The product tool of the MCP surface (#1786): create a root product —
    /// the first step of the agent-installation scenario (the product is what
    /// an HSM agent's access key belongs to). A thin delegation to
    /// <see cref="ProductAdministrationService"/>, the same engine the REST
    /// create endpoint runs on.
    /// </summary>
    // The authorization is REUSED, not re-implemented: the service resolves
    // the boundary-less create gate through IApiTokenAuthorizationService (any
    // live read-write caller, with the security-event recording inside).
    // Read-write enforcement needs no tool-level check: /mcp serves every
    // message via POST, and the HsmApiToken management policy rejects
    // read-only tokens on unsafe methods outright.
    [McpServerToolType]
    public sealed class ProductsMcpTools
    {
        private readonly ProductAdministrationService _writer;
        private readonly IHttpContextAccessor _http;

        public ProductsMcpTools(ProductAdministrationService writer, IHttpContextAccessor http)
        {
            _writer = writer;
            _http = http;
        }


        // Idempotent=false: a create — re-sending it after a network retry
        // would create a second product (or answer the duplicate-name error).
        [McpServerTool(Name = "create_product", ReadOnly = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
        [Description("Creates a root product — the container an HSM agent's access key belongs to; the first step of installing an agent. Any read-write token may create one; the token's owner becomes the product's author, and the product is created with a default full-permission access key. The name must be 1..100 characters of '0-9 a-z A-Z space .,_-=#:;%&*()', unique among root products (the response echoes the created product).")]
        public async System.Threading.Tasks.Task<ProductDto> CreateProductAsync(
            [Description("Product name: 1..100 characters of '0-9a-zA-Z .,_-=#:;%&*()', unique among root products. Surrounding whitespace is trimmed.")] string name) =>
            McpToolWriteErrors.Unwrap(await _writer.CreateProductAsync(name, User));


        // The shared ambient-principal accessor (see McpToolContext); a property
        // so the tool bodies read like their REST twins' `User`.
        private ClaimsPrincipal User => McpToolContext.UserOf(_http);
    }
}
