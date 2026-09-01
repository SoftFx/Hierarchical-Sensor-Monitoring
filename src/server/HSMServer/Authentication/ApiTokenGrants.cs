using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Authentication
{
    // Canonicalizes and validates token grant lists before any persistence or authorization
    // decision. Operations must come from the catalog; boundary kinds must be known enum
    // values; Global carries no boundary id while Product/Folder carry a canonical Guid id;
    // duplicate operation+boundary pairs are rejected. Anything unknown fails closed.
    // Canonical grants are returned in a deterministic order (operation, then boundary) so
    // serialized records are stable, and as an ImmutableArray so no consumer can mutate a
    // live token's grant set in place.
    public static class ApiTokenGrants
    {
        // Upper bound on grants per token: canonicalization allocates per grant and the
        // persisted row is rescanned into memory at every boot, so the grant list must not
        // be the one unbounded caller-supplied input on this surface.
        public const int MaxGrants = 1024;


        // `problem` names the first offending grant and why it failed, so a load-time skip
        // can log something an operator can act on ("operation 'x' is not in the catalog")
        // instead of a bare entity id. Null on success.
        public static bool TryCanonicalize(IEnumerable<ApiTokenGrantEntity> grants, out ImmutableArray<ApiTokenGrantEntity> canonical, out string problem)
        {
            canonical = default;
            problem = null;

            var result = new List<ApiTokenGrantEntity>();
            var seen = new HashSet<ApiTokenGrantEntity>();

            foreach (var grant in grants ?? Array.Empty<ApiTokenGrantEntity>())
            {
                if (grant is null)
                {
                    problem = "null grant entry";
                    return false;
                }

                // Fail closed before allocating further once the bound is exceeded.
                if (seen.Count >= MaxGrants)
                {
                    problem = $"more than {MaxGrants} grants";
                    return false;
                }

                if (!ApiTokenOperations.IsValid(grant.Operation))
                {
                    problem = $"operation '{grant.Operation ?? "<null>"}' is not in the catalog";
                    return false;
                }

                // Generic overload: allocation-free, and a change of the enum's underlying
                // type can no longer turn this into a throw instead of a fail-closed false.
                if (!Enum.IsDefined((ApiTokenBoundaryKind)grant.BoundaryKind))
                {
                    problem = $"operation '{grant.Operation}' has unknown boundary kind {grant.BoundaryKind}";
                    return false;
                }

                var kind = (ApiTokenBoundaryKind)grant.BoundaryKind;
                string boundaryId = null;

                if (kind == ApiTokenBoundaryKind.Global)
                {
                    // The global boundary is explicit and id-less; an id here is malformed.
                    if (!string.IsNullOrEmpty(grant.BoundaryId))
                    {
                        problem = $"operation '{grant.Operation}' at Global carries a boundary id";
                        return false;
                    }
                }
                else
                {
                    // Resource-scoped boundaries are concrete stable ids, canonicalized to the
                    // Guid "D" form; no wildcards survive validation, and the empty guid is
                    // not a resource.
                    if (grant.BoundaryId is null || !Guid.TryParse(grant.BoundaryId, out var id) || id == Guid.Empty)
                    {
                        problem = $"operation '{grant.Operation}' has an invalid {kind} boundary id '{grant.BoundaryId ?? "<null>"}'";
                        return false;
                    }

                    boundaryId = id.ToString();
                }

                // Operations that only mean something at certain boundaries (e.g. a
                // server-wide system-health read) never reach storage paired with a
                // boundary they cannot match.
                if (!ApiTokenOperations.IsValidBoundary(grant.Operation, kind))
                {
                    problem = $"operation '{grant.Operation}' is not grantable at boundary {kind}";
                    return false;
                }

                var canonicalGrant = new ApiTokenGrantEntity
                {
                    Operation = grant.Operation,
                    BoundaryKind = grant.BoundaryKind,
                    BoundaryId = boundaryId,
                };

                // Records compare by value, so this rejects duplicate operation+boundary pairs.
                if (!seen.Add(canonicalGrant))
                {
                    problem = $"duplicate grant '{grant.Operation}' at {kind} boundary '{boundaryId}'";
                    return false;
                }

                result.Add(canonicalGrant);
            }

            result.Sort(CompareGrants);

            canonical = result.ToImmutableArray();

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
