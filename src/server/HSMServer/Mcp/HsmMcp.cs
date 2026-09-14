using System;

namespace HSMServer.Mcp
{
    // Shared constants of the MCP surface (#1391). The endpoint path lives here
    // because it is MCP's own concern; the guards and Program.cs reference it.
    public static class HsmMcp
    {
        // Streamable HTTP endpoint served by HSMServer itself on the SitePort,
        // outside the /api/v1 area guard (JSON-RPC errors, SDK-owned handler)
        // but inside the bearer-token credential family.
        public const string EndpointPath = "/mcp";

        // Tool list convention (#1391): no pagination cursors — every list tool
        // takes a single `limit` (default 20, cap 200) and answers `totalFound`,
        // so an agent knows whether to narrow without chasing pages.
        public const int DefaultLimit = 20;
        public const int MaxLimit = 200;

        public static int NormalizeLimit(int limit) =>
            limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);
    }
}
