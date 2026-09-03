using System;
using System.Collections.Immutable;
using System.Security.Claims;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMDatabase.AccessManager;

namespace HSMServer.Authentication
{
    // Resource-authorization evaluator of the management API (initiative step 3). Every
    // call recomputes BOTH sides of the intersection from the authoritative stores:
    //
    //     allowed(operation, resource) = ownerCurrentlyAllows(operation, resource)
    //                                 AND tokenGrantAllows(operation, currentBoundary(resource))
    //
    // Nothing is cached: owner downgrade/deletion, role removal, resource moves and token
    // revocation between requests take effect on the very next evaluation.
    public interface IApiTokenAuthorizationService
    {
        // Decision for a concrete operation on a concrete target, with the documented
        // 403/404 split (see ApiTokenAuthorization).
        ApiTokenAuthorization Authorize(ClaimsPrincipal principal, string operation, ApiTokenResource resource);

        // List-filtering predicate: the target is listable under the given operation —
        // the same conjunction Authorize applies (owner sight, operation grant at the
        // current boundary, owner capability) minus the security-event recording: a
        // list that returns full bodies must not disclose items whose item endpoint
        // would 403, and list filtering is not a probe signal. Out-of-reach or
        // ungranted targets are simply not listed, never 403-per-item.
        bool IsVisible(ClaimsPrincipal principal, string operation, ApiTokenResource resource);

        // Caller-wide gate for GLOBAL resources (alert schedules): the caller may act
        // when ANY of the token's grants for the operation sits at a boundary that
        // currently passes the list predicate above. Candidate boundaries are
        // enumerated from the token's own grants INSIDE the evaluator — callers never
        // touch grant records. A denial is recorded ONCE, as AuthorizationDenied: the
        // gate is caller-wide and answers 403, so it must not feed the
        // enumeration-probe signal (AuthorizationNotFound) that per-target 404s carry.
        bool HasOperationAtAnyVisibleBoundary(ClaimsPrincipal principal, string operation);

        // Whether the operation is granted to a LIVE caller at the GLOBAL boundary
        // under an admin owner — the token-side "everywhere" shape. Scoped-resource
        // decisions deliberately do NOT treat it as a wildcard (a Global grant never
        // covers Product/Folder targets); the one sanctioned use is a short-circuit
        // for callers that already passed the caller-wide gate above, so a filter
        // over scoped items does not hand the broadest token an empty-everywhere
        // result. Records nothing, like IsVisible.
        bool HasOperationAtGlobalScope(ClaimsPrincipal principal, string operation);
    }


    public sealed class ApiTokenAuthorizationService : IApiTokenAuthorizationService
    {
        private readonly IUserManager _users;
        private readonly IApiTokenManager _tokens;
        private readonly IFolderManager _folders;
        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenSecurityEventSink _securityEvents;


        public ApiTokenAuthorizationService(IUserManager users, IApiTokenManager tokens,
            IFolderManager folders, ITreeValuesCache cache, IApiTokenSecurityEventSink securityEvents)
        {
            _users = users;
            _tokens = tokens;
            _folders = folders;
            _cache = cache;
            _securityEvents = securityEvents;
        }


        public ApiTokenAuthorization Authorize(ClaimsPrincipal principal, string operation, ApiTokenResource resource)
        {
            if (!TryResolveCaller(principal, out var owner, out var grants))
            {
                Record(principal, operation, resource, ApiTokenAuthorization.NotFound);
                return ApiTokenAuthorization.NotFound;
            }

            if (!TryResolveBoundary(resource, out var boundary))
            {
                Record(principal, operation, resource, ApiTokenAuthorization.NotFound);
                return ApiTokenAuthorization.NotFound;
            }

            // 404 first: absent, invisible to the owner, or entirely outside the token's
            // reach — indistinguishable so callers cannot enumerate resources.
            if (!OwnerCanSee(owner, boundary))
            {
                Record(principal, operation, resource, ApiTokenAuthorization.NotFound);
                return ApiTokenAuthorization.NotFound;
            }

            if (!TokenReachesBoundary(grants, boundary))
            {
                Record(principal, operation, resource, ApiTokenAuthorization.NotFound);
                return ApiTokenAuthorization.NotFound;
            }

            // 403: the target is known and in reach, but this operation is not granted or
            // the owner currently cannot perform it.
            if (!TokenGrantsOperation(grants, operation, boundary))
            {
                Record(principal, operation, resource, ApiTokenAuthorization.Forbidden);
                return ApiTokenAuthorization.Forbidden;
            }

            if (!OwnerCanPerform(owner, operation, boundary))
            {
                Record(principal, operation, resource, ApiTokenAuthorization.Forbidden);
                return ApiTokenAuthorization.Forbidden;
            }

            return ApiTokenAuthorization.Allowed;
        }

        public bool IsVisible(ClaimsPrincipal principal, string operation, ApiTokenResource resource) =>
            TryResolveCaller(principal, out var owner, out var grants) &&
            IsVisibleCore(owner, grants, operation, resource);

        public bool HasOperationAtAnyVisibleBoundary(ClaimsPrincipal principal, string operation)
        {
            // The caller is resolved once; the per-candidate decision is the plain list
            // predicate, so a Global grant still requires an admin owner and a scoped
            // grant still requires the boundary to resolve and the owner to see it.
            var allowed = TryResolveCaller(principal, out var owner, out var grants) &&
                GrantsOperationAtAnyVisibleBoundary(owner, grants, operation);

            if (!allowed)
                Record(principal, operation, ApiTokenResource.GlobalScope, ApiTokenAuthorization.Forbidden);

            return allowed;
        }

        public bool HasOperationAtGlobalScope(ClaimsPrincipal principal, string operation) =>
            TryResolveCaller(principal, out var owner, out var grants) &&
            IsVisibleCore(owner, grants, operation, ApiTokenResource.GlobalScope);

        private bool GrantsOperationAtAnyVisibleBoundary(User owner,
            ImmutableArray<ApiTokenGrantEntity> grants, string operation)
        {
            foreach (var grant in grants)
            {
                if (grant.Operation != operation || !TryGrantResource(grant, out var resource))
                    continue;

                if (IsVisibleCore(owner, grants, operation, resource))
                    return true;
            }

            return false;
        }

        private bool IsVisibleCore(User owner, ImmutableArray<ApiTokenGrantEntity> grants,
            string operation, ApiTokenResource resource) =>
            TryResolveBoundary(resource, out var boundary) &&
            OwnerCanSee(owner, boundary) &&
            TokenGrantsOperation(grants, operation, boundary) &&
            OwnerCanPerform(owner, operation, boundary);

        // Denials reach the append-only security-event sink with the safe identifiers the
        // design names: token id, subject id, required permission, safe target id — and
        // with the decision preserved: 404 denials (invisible/out-of-reach targets, the
        // enumeration-probe signal) are AuthorizationNotFound, 403 scope denials are
        // AuthorizationDenied. Allowed decisions are not per-request events.
        private void Record(ClaimsPrincipal principal, string operation, ApiTokenResource resource,
            ApiTokenAuthorization decision)
        {
            var tokenId = principal?.FindFirst(HsmApiTokenClaims.TokenId)?.Value;
            var ownerText = principal?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;
            Guid? ownerId = Guid.TryParse(ownerText, out var parsed) ? parsed : null;

            var kind = decision == ApiTokenAuthorization.NotFound
                ? ApiTokenSecurityEventKind.AuthorizationNotFound
                : ApiTokenSecurityEventKind.AuthorizationDenied;

            _securityEvents.Record(new ApiTokenSecurityEvent(
                kind,
                tokenId, ownerId, operation,
                TargetId: $"{resource.Kind}:{resource.Id}"));
        }


        private bool TryResolveCaller(ClaimsPrincipal principal, out User owner, out ImmutableArray<ApiTokenGrantEntity> grants)
        {
            owner = null;
            grants = default;

            // The principal shape is enforced upstream by the management policy; anything
            // else fails closed rather than throwing.
            var ownerClaim = principal?.FindFirst(HsmApiTokenClaims.OwnerUserId);
            var tokenClaim = principal?.FindFirst(HsmApiTokenClaims.TokenId);
            if (ownerClaim is null || tokenClaim is null ||
                !Guid.TryParse(ownerClaim.Value, out var ownerId))
                return false;

            owner = _users[ownerId];

            // Liveness re-check through the manager's sanctioned predicate: a token
            // revoked, expired or generation-invalidated between authentication and this
            // authorization fails closed here, not on the next request.
            if (!_tokens.IsTokenLive(tokenClaim.Value))
                return false;

            var token = _tokens.GetToken(tokenClaim.Value);

            if (owner is null || token is null)
                return false;

            grants = token.Grants;
            return true;
        }

        // Current authorization anchor of a target, resolved from the live hierarchy.
        // Text forms of the ids are precomputed once: grant BoundaryIds are canonical
        // Guid strings, so per-grant ToString would allocate on every comparison of every
        // request.
        private sealed record AuthorizationBoundary(ApiTokenResourceKind Kind, Guid Id, Guid? FolderId)
        {
            public string IdText { get; } = Id.ToString();

            public string FolderText { get; } = FolderId?.ToString();
        }

        // Resolves the target to its CURRENT authorization boundary from the live
        // hierarchy: sensor -> its product, product -> itself plus its current folder.
        // Deleted/unknown ids fail closed (false).
        private bool TryResolveBoundary(ApiTokenResource resource, out AuthorizationBoundary boundary)
        {
            boundary = default;

            switch (resource.Kind)
            {
                case ApiTokenResourceKind.Global:
                    boundary = new(ApiTokenResourceKind.Global, Guid.Empty, null);
                    return true;

                case ApiTokenResourceKind.Product:
                {
                    if (!_cache.TryGetProduct(resource.Id, out var product) || product is null)
                        return false;

                    boundary = new(ApiTokenResourceKind.Product, resource.Id, product.FolderId);
                    return true;
                }

                case ApiTokenResourceKind.Sensor:
                {
                    // A parentless sensor cannot resolve a product boundary: fail closed
                    // rather than trusting the node itself. Note Root would CAST a
                    // parentless sensor to ProductModel and throw — go through Parent so
                    // the defensive path actually returns false.
                    var sensor = _cache.GetSensor(resource.Id);
                    var product = sensor?.Parent?.Root;

                    if (product is null)
                        return false;

                    boundary = new(ApiTokenResourceKind.Product, product.Id, product.FolderId);
                    return true;
                }

                case ApiTokenResourceKind.Folder:
                {
                    if (!_folders.TryGetValue(resource.Id, out var folder) || folder is null)
                        return false;

                    boundary = new(ApiTokenResourceKind.Folder, resource.Id, null);
                    return true;
                }

                default:
                    return false;
            }
        }

        // Owner visibility: any assignment at the boundary (or IsAdmin). For a Product this
        // mirrors the app's own rule (User.IsProductAvailable = IsAdmin ||
        // IsUserProduct) with NO folder fallback: HSM materialises folder roles into
        // per-product ProductsRoles entries at grant time and at move time
        // (FoldersController/FolderManager), and per-product narrowing
        // (ProductController.RemoveUserRole/EditUserRole) edits ProductsRoles only — a
        // folder fallback here would resurrect rights the owner was explicitly revoked
        // from, making the token side exceed the owner's current rights.
        private static bool OwnerCanSee(User owner, AuthorizationBoundary boundary) =>
            owner.IsAdmin || boundary.Kind switch
            {
                ApiTokenResourceKind.Global => false, // global operations are admin-only
                ApiTokenResourceKind.Product => owner.IsUserProduct(boundary.Id),
                ApiTokenResourceKind.Folder => owner.IsFolderAvailable(boundary.Id),
                _ => false,
            };

        // Owner capability for the operation: writes need the Manager role at the
        // boundary; reads need exactly the visibility checked above. No folder fallback
        // for products, for the same materialisation reason as OwnerCanSee — the owner
        // side never exceeds what the app's own IsManager check grants.
        private static bool OwnerCanPerform(User owner, string operation, AuthorizationBoundary boundary)
        {
            if (owner.IsAdmin)
                return true;

            if (!ApiTokenOperations.IsWrite(operation))
                return true; // OwnerCanSee already established a read-level role

            return boundary.Kind switch
            {
                ApiTokenResourceKind.Product => owner.IsManager(boundary.Id),
                ApiTokenResourceKind.Folder => owner.IsFolderManager(boundary.Id),
                _ => false, // writes at the global boundary are admin-only
            };
        }

        // Whether ANY grant of the token is anchored at the target's current boundary —
        // the reach test that keeps out-of-scope targets 404. A Global grant never counts
        // as a wildcard over Product/Folder targets ("all boundaries" expands to concrete
        // ids at creation and is never persisted as a wildcard).
        private static bool TokenReachesBoundary(ImmutableArray<ApiTokenGrantEntity> grants,
            AuthorizationBoundary boundary) =>
            AnyGrantAt(grants, boundary, operation: null);

        private static bool TokenGrantsOperation(ImmutableArray<ApiTokenGrantEntity> grants, string operation,
            AuthorizationBoundary boundary) =>
            AnyGrantAt(grants, boundary, operation);

        private static bool AnyGrantAt(ImmutableArray<ApiTokenGrantEntity> grants,
            AuthorizationBoundary boundary, string operation)
        {
            foreach (var grant in grants)
            {
                if (operation is not null && grant.Operation != operation)
                    continue;

                var kind = (ApiTokenBoundaryKind)grant.BoundaryKind;

                switch (boundary.Kind)
                {
                    case ApiTokenResourceKind.Global:
                        if (kind == ApiTokenBoundaryKind.Global)
                            return true;
                        break;

                    case ApiTokenResourceKind.Product:
                        // Explicit product grant, or a folder grant over the product's
                        // CURRENT folder (the only dynamic-membership case).
                        if (kind == ApiTokenBoundaryKind.Product && grant.BoundaryId == boundary.IdText)
                            return true;

                        if (kind == ApiTokenBoundaryKind.Folder && boundary.FolderText is not null &&
                            grant.BoundaryId == boundary.FolderText)
                            return true;
                        break;

                    case ApiTokenResourceKind.Folder:
                        if (kind == ApiTokenBoundaryKind.Folder && grant.BoundaryId == boundary.IdText)
                            return true;
                        break;
                }
            }

            return false;
        }

        // A grant's own boundary as an authorization target — the inverse of the
        // matching above, used to enumerate the candidate boundaries of the
        // caller-wide gate. Malformed pairs (unknown kind, unparsable id, Global with
        // an id) are rejected by ApiTokenGrants.TryCanonicalize at persistence and at
        // load, so they cannot reach a live token; skipping them here is defense in
        // depth that fails closed.
        private static bool TryGrantResource(ApiTokenGrantEntity grant, out ApiTokenResource resource)
        {
            switch ((ApiTokenBoundaryKind)grant.BoundaryKind)
            {
                case ApiTokenBoundaryKind.Global when string.IsNullOrEmpty(grant.BoundaryId):
                    resource = ApiTokenResource.GlobalScope;
                    return true;

                case ApiTokenBoundaryKind.Product when Guid.TryParse(grant.BoundaryId, out var productId):
                    resource = ApiTokenResource.Product(productId);
                    return true;

                case ApiTokenBoundaryKind.Folder when Guid.TryParse(grant.BoundaryId, out var folderId):
                    resource = ApiTokenResource.Folder(folderId);
                    return true;

                default:
                    resource = null;
                    return false;
            }
        }
    }
}
