using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi
{
    // List envelope of the management API: 1-based page, server-clamped size, stable
    // per-resource ordering, camelCase via MVC defaults.
    public sealed record ApiPageDto<T>
    {
        public List<T> Items { get; init; } = [];

        public int Page { get; init; }

        public int PageSize { get; init; }

        public int TotalCount { get; init; }

        public int TotalPages { get; init; }
    }
}
