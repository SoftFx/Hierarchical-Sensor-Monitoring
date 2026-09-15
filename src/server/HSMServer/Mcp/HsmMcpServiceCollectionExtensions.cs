using HSMServer.ServerConfiguration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace HSMServer.Mcp
{
    // The MCP server registration in one place (#1391), the
    // HsmApiTokenServiceCollectionExtensions pattern: the endpoint mapping and
    // the guards stay with Program.cs/ApplicationServiceExtensions, the server +
    // tool wiring lives here so the surface is unit-testable in isolation (see
    // HsmMcpServerRegistrationTests).
    public static class HsmMcpServiceCollectionExtensions
    {
        public static IServiceCollection AddHsmMcpServer(this IServiceCollection services)
        {
            // The tools resolve the caller through the ambient HTTP context
            // (McpToolContext), which holds only while every tools/call executes
            // INLINE within its POST — see the stateless pin below.
            services.AddHttpContextAccessor();

            return services.AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation
                    {
                        Name = "HSMServer",
                        Title = "HSM monitoring server",
                        Version = ServerConfig.Version,
                    };
                })
                // Statelessly hosted Streamable HTTP: the tools are pure reads, so
                // no session affinity is needed. Set explicitly (the 2.2.0 default
                // is stateless since the 2026-07-28 protocol revision, but an SDK
                // default change would otherwise silently degrade every tool
                // call); pinned by HsmMcpServerRegistrationTests.
                .WithHttpTransport(options => options.Stateless = true)
                .WithTools<SensorTreeMcpTools>()
                .WithTools<AlertsMcpTools>()
                .Services;
        }
    }
}
