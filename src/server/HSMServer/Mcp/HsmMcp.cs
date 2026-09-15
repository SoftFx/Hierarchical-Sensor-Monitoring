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
        // so an agent knows whether to narrow without chasing pages. Where a
        // list has NO narrowing dimension (products, alert templates and
        // schedules — nothing to filter by), it also takes a `page`, mirroring
        // get_node's foldersPage: beyond the cap, page is the only reachability
        // (#1392 review). find_sensors stays cursorless on purpose — it has
        // search/type/productId to narrow with.
        public const int DefaultLimit = 20;
        public const int MaxLimit = 200;

        // A tool result lands directly in the calling model's context window
        // (#1392 review), so the history tool defaults tighter than the REST
        // twin's 1000 (a programmatic client pages; a model should narrow).
        // The same argument bounds the CEILING tighter than the REST twin's
        // 10000: an explicit maxPoints from a naive agent must not drag a
        // String sensor's unbounded per-point payloads into the context.
        public const int DefaultMaxPoints = 200;
        public const int HistoryMaxPointsLimit = 2_000;

        public static int NormalizeLimit(int limit) =>
            limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

        // 1-based, like every REST page of the area.
        public static int NormalizePage(int page) => Math.Max(page, 1);
    }
}
