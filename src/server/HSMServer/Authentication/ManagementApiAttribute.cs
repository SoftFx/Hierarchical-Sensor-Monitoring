using System;

namespace HSMServer.Authentication
{
    // Marks a controller/action as part of the versioned management API (/api/v1). The
    // ManagementApiGuardMiddleware allow-lists /api/v1 endpoints by this marker plus the
    // required authorization policy, so a route added to the area without both is 404 by
    // default. Ordinary data-management controllers pair it with
    // [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]; the reserved cookie-only
    // /api/v1/api-tokens family pairs it with the default cookie authorization.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ManagementApiAttribute : Attribute;
}
