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
        // so an agent knows whether to narrow without chasing pages. Every list
        // ALSO takes a `page` (mirroring get_node's foldersPage): narrowing is
        // the preferred move, but it is not guaranteed — get_node's own sensors
        // list caps at 200 without paging, and uniformly-named sensors cannot
        // be partitioned by search at all — so beyond the cap, `page` is the
        // only reachability (#1392 review, rounds 3-4). The paging tools share
        // the REST clamps (ApiPagination): a page past the end serves the LAST
        // page, and the Skip arithmetic can never wrap int. Because the clamps
        // REWRITE the caller's limit silently, every list envelope echoes the
        // effective `limit`, the `page` actually served and `totalPages` — the
        // agent must divide by the limit the server applied, never the one it
        // asked for (#1392 review, round 5).
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
