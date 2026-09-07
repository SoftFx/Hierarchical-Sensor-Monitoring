using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi
{
    /// <summary>
    /// List envelope of every management API collection endpoint: 1-based page,
    /// server-clamped size, stable per-resource ordering.
    /// </summary>
    public sealed record ApiPageDto<T>
    {
        /// <summary>The page's items.</summary>
        public List<T> Items { get; init; } = [];

        /// <summary>1-based page number (a page request beyond the end returns the last page).</summary>
        public int Page { get; init; }

        /// <summary>Effective page size (1..200; default 50).</summary>
        public int PageSize { get; init; }

        /// <summary>Total item count across all pages.</summary>
        public int TotalCount { get; init; }

        /// <summary>Total page count (0 when the collection is empty).</summary>
        public int TotalPages { get; init; }
    }
}
