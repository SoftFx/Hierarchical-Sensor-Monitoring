using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

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


        // Frozen on purpose: immutable at the type level (no consumer can Add a capability
        // the file says must never exist) and faster on the per-request IsValid path.
        private static readonly FrozenSet<string> _all = new[]
        {
            ProductsRead, ProductsWrite,
            SensorsRead, SensorsWrite,
            HistoryRead,
            AlertsRead, AlertsWrite,
            DashboardsRead, DashboardsWrite,
            NotificationsRead, NotificationsWrite,
            SystemHealthRead,
        }.ToFrozenSet();


        // A snapshot, never the live set: IReadOnlyCollection over a mutable collection
        // can be cast back and mutated.
        public static IReadOnlyCollection<string> All => _all.ToArray();


        // Exact ordinal match against the catalog; absence means denied.
        public static bool IsValid(string operation) =>
            !string.IsNullOrEmpty(operation) && _all.Contains(operation);
    }
}
