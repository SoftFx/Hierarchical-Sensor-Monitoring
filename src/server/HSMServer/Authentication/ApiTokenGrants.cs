using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Authentication
{
    // Canonicalizes and validates token grant lists before any persistence or authorization
    // decision. Operations must come from the catalog; boundary kinds must be known enum
    // values; Global carries no boundary id while Product/Folder carry a canonical Guid id;
    // duplicate operation+boundary pairs are rejected. Anything unknown fails closed.
    // Canonical grants are returned in a deterministic order (operation, then boundary) so
    // serialized records are stable.
    public static class ApiTokenGrants
    {
        // Upper bound on grants per token: canonicalization allocates per grant and the
        // persisted row is rescanned into memory at every boot, so the grant list must not
        // be the one unbounded caller-supplied input on this surface.
        public const int MaxGrants = 1024;


        public static bool TryCanonicalize(IEnumerable<ApiTokenGrantEntity> grants, out List<ApiTokenGrantEntity> canonical)
        {
            canonical = null;

            var result = new List<ApiTokenGrantEntity>();
            var seen = new HashSet<ApiTokenGrantEntity>();

            foreach (var grant in grants ?? Array.Empty<ApiTokenGrantEntity>())
            {
                if (grant is null)
                    return false;

                // Fail closed before allocating further once the bound is exceeded.
                if (seen.Count >= MaxGrants)
                    return false;

                if (!ApiTokenOperations.IsValid(grant.Operation))
                    return false;

                // Generic overload: allocation-free, and a change of the enum's underlying
                // type can no longer turn this into a throw instead of a fail-closed false.
                if (!Enum.IsDefined((ApiTokenBoundaryKind)grant.BoundaryKind))
                    return false;

                var kind = (ApiTokenBoundaryKind)grant.BoundaryKind;
                string boundaryId = null;

                if (kind == ApiTokenBoundaryKind.Global)
                {
                    // The global boundary is explicit and id-less; an id here is malformed.
                    if (!string.IsNullOrEmpty(grant.BoundaryId))
                        return false;
                }
                else
                {
                    // Resource-scoped boundaries are concrete stable ids, canonicalized to the
                    // Guid "D" form; no wildcards survive validation.
                    if (grant.BoundaryId is null || !Guid.TryParse(grant.BoundaryId, out var id))
                        return false;

                    boundaryId = id.ToString();
                }

                var canonicalGrant = new ApiTokenGrantEntity
                {
                    Operation = grant.Operation,
                    BoundaryKind = grant.BoundaryKind,
                    BoundaryId = boundaryId,
                };

                // Records compare by value, so this rejects duplicate operation+boundary pairs.
                if (!seen.Add(canonicalGrant))
                    return false;

                result.Add(canonicalGrant);
            }

            result.Sort(CompareGrants);

            canonical = result;

            return true;
        }


        private static int CompareGrants(ApiTokenGrantEntity left, ApiTokenGrantEntity right)
        {
            var byOperation = string.CompareOrdinal(left.Operation, right.Operation);

            if (byOperation != 0)
                return byOperation;

            var byKind = left.BoundaryKind.CompareTo(right.BoundaryKind);

            if (byKind != 0)
                return byKind;

            return string.CompareOrdinal(left.BoundaryId, right.BoundaryId);
        }
    }
}
