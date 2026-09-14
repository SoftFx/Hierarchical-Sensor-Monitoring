using System;

namespace HSMServer.Model.ManagementApi
{
    // The shared clamp of every /api/v1 list (#1387 review, round 4 — the
    // sequence was triplicated verbatim across the sensor-tree controllers,
    // overflow comment included). The templates/schedules controllers still
    // carry their own copies from before this helper existed; adopting it there
    // is a follow-up, new area controllers must use this one.
    public static class ApiPagination
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;


        /// <summary>Normalizes a page/size pair: page ≥ 1; size falls back to the default when non-positive and never exceeds the ceiling.</summary>
        public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
            (Math.Max(page, 1), Math.Min(pageSize <= 0 ? DefaultPageSize : pageSize, MaxPageSize));


        public static int TotalPagesOf(int totalCount, int pageSize) =>
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);


        // Clamp the page into [1, totalPages]: unchecked (page - 1) * pageSize
        // would overflow int for huge page numbers, and a NEGATIVE Skip count
        // silently returns the FIRST page labeled as page N.
        public static int ClampPage(int page, int totalPages) =>
            Math.Min(page, Math.Max(totalPages, 1));
    }
}
