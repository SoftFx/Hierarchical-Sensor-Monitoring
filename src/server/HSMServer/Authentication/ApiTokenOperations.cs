using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Authentication
{
    // Canonical permission catalog for grantable management operations (v1; illustrative
    // until the API capability inventory is approved by the initiative review). Read and
    // write are always separate; wildcards, "admin", controller-name permissions and
    // implicit write-through-read permissions are forbidden. Credential-bearing capabilities
    // (users:*, access-keys:*, credentials:*, server-settings:*) are deliberately absent
    // from v1 and must never be added without a separate threat review.
    public static class ApiTokenOperations
    {
        public const string ProductsRead = "products:read";
        public const string ProductsWrite = "products:write";
        public const string SensorsRead = "sensors:read";
        public const string SensorsWrite = "sensors:write";
        public const string HistoryRead = "history:read";
        public const string AlertsRead = "alerts:read";
        public const string AlertsWrite = "alerts:write";
        public const string DashboardsRead = "dashboards:read";
        public const string DashboardsWrite = "dashboards:write";
        public const string NotificationsRead = "notifications:read";
        public const string NotificationsWrite = "notifications:write";
        public const string SystemHealthRead = "system-health:read";


        private static readonly string[] _allItems =
        [
            ProductsRead, ProductsWrite,
            SensorsRead, SensorsWrite,
            HistoryRead,
            AlertsRead, AlertsWrite,
            DashboardsRead, DashboardsWrite,
            NotificationsRead, NotificationsWrite,
            SystemHealthRead,
        ];

        // Frozen on purpose: immutable at the type level (no consumer can Add a capability
        // the file says must never exist) and faster on the per-request IsValid path.
        private static readonly FrozenSet<string> _all = _allItems.ToFrozenSet();

        // Cached snapshot in catalog order: IReadOnlyCollection over a mutable collection
        // can be cast back and mutated, and a per-read ToArray() allocates for nothing.
        private static readonly ImmutableArray<string> _allSnapshot = _allItems.ToImmutableArray();


        public static IReadOnlyCollection<string> All => _allSnapshot;

        // Boundary kinds an operation may be granted at. An operation that only means
        // something server-wide (system-health:read) must never reach storage bound to a
        // Product or Folder id — a pair the authorization evaluator could only answer with
        // "never matches" is rejected in the layer that already owns fail-closed
        // validation. Operations absent from the map grant at any boundary.
        private static readonly FrozenDictionary<string, ApiTokenBoundaryKind[]> _allowedBoundaries =
            new Dictionary<string, ApiTokenBoundaryKind[]>
            {
                [SystemHealthRead] = [ApiTokenBoundaryKind.Global],
            }.ToFrozenDictionary();


        public static bool IsValid(string operation) =>
            !string.IsNullOrEmpty(operation) && _all.Contains(operation);

        // The catalog's naming discipline is "<resource>:read" / "<resource>:write" with
        // read and write always separate, so the required owner role derives from the
        // name: writes need the Manager role at the boundary (or IsAdmin), reads any
        // assigned role. Canonical place for the rule so operation additions cannot
        // silently change the privilege requirement.
        public static bool IsWrite(string operation) =>
            operation is not null && operation.EndsWith(":write", System.StringComparison.Ordinal);


        // True when the operation may be granted at the given boundary kind. This is the
        // canonical place to tighten operation/boundary semantics as the catalog grows.
        public static bool IsValidBoundary(string operation, ApiTokenBoundaryKind kind) =>
            !_allowedBoundaries.TryGetValue(operation, out var allowed) || allowed.Contains(kind);
    }
}
