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
            // Statelessly hosted Streamable HTTP: the tools are pure reads, so no
            // session affinity is needed. The tools resolve the caller through
            // the ambient HTTP context (McpToolContext).
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
                .WithHttpTransport()
                .WithTools<SensorTreeMcpTools>()
                .WithTools<AlertsMcpTools>()
                .Services;
        }
    }
}
