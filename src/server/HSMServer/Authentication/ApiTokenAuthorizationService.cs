using System;
using System.Linq;
using System.Security.Claims;
using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.Authentication;

namespace HSMServer.Authentication
{
    // Resource-authorization evaluator of the management API. Since #1384 a token is a
    // full mirror of its owner: every call recomputes the OWNER side from the
    // authoritative stores, and the token side contributes exactly one fact — whether
    // the credential is read-only.
    //
    //     read allowed(resource)  = ownerCurrentlySees(resource)
    //     write allowed(resource) = ownerCurrentlySees(resource)
    //                              AND ownerCanWriteAt(resource)
    //                              AND !token.ReadOnly
    //
    // Nothing is cached: owner downgrade/deletion, role removal, resource moves and
    // token revocation between requests take effect on the very next evaluation.
    public interface IApiTokenAuthorizationService
    {
        // Decision for a read on a concrete target, with the documented 403/404 split
        // (see ApiTokenAuthorization). A read-only token never fails here — the flag
        // constrains writes only.
        ApiTokenAuthorization AuthorizeRead(ClaimsPrincipal principal, ApiTokenResource resource);

        // Decision for a write on a concrete target: NotFound when the owner cannot see
        // the target; Forbidden when the owner sees it but cannot write there (Viewer
        // role) or the token is read-only.
        ApiTokenAuthorization AuthorizeWrite(ClaimsPrincipal principal, ApiTokenResource resource);

        // List-filtering predicate: the target is listable — the owner-sight half of
        // AuthorizeRead minus the security-event recording: a list that returns full
        // bodies must not disclose items whose item endpoint would refuse them, and
        // list filtering is not a probe signal. Out-of-sight targets are simply not
        // listed, never 403-per-item.
        bool IsVisible(ClaimsPrincipal principal, ApiTokenResource resource);

        // Caller-wide gate for GLOBAL resources (alert schedules): the owner is an
        // admin or holds a role that currently RESOLVES (a stale entry pointing at a
        // deleted product/folder counts for nothing). A denial is
        // recorded ONCE, as AuthorizationDenied: the gate is caller-wide and answers
        // 403, so it must not feed the enumeration-probe signal
        // (AuthorizationNotFound) that per-target 404s carry.
        bool CanSeeAnyBoundary(ClaimsPrincipal principal);

        // Per-item body-shaping predicate of the chat read surface: the owner
        // is an admin or currently holds the Manager role at one of the
        // folders the chat is bound to — the token-side twin of the web UI's
        // EditChat gate (TelegramRoleFilterById, ProductManager). A GLOBAL
        // chat passes no folders, so its detail is admin-only, exactly like
        // the UI gate whose folder loop finds nothing to match. Role-only by
        // design: the token's read-only flag never constrains reads, and like
        // IsVisible this records nothing — it shapes bodies inside an
        // already-authorized list, it is not a probe signal.
        bool CanSeeChatDetail(ClaimsPrincipal principal, System.Collections.Generic.IReadOnlyCollection<Guid> chatFolderIds);

        // The folder twin of CanSeeChatDetail: the owner is an admin or
        // currently holds the Manager role at the folder — the token-side
        // twin of the web UI's folder-detail gate (FoldersController's
        // FolderRoleFilterByFolderId, ProductManager). Same properties:
        // role-only (reads never consult the write flag) and audit-silent.
        bool CanSeeFolderDetail(ClaimsPrincipal principal, Guid folderId);

        // Decision for creating a root product — the one management write
        // with NO boundary to authorize against (the product does not exist
        // yet). Mirrors the web UI's rule that any authenticated user may
        // create a product and become its author: any LIVE caller with a
        // read-write token is allowed, role-less owners included (the
        // creation service then grants the creator the ProductManager role
        // on the new product, so what a role-less owner creates is
        // immediately visible and manageable by them). A denial is recorded
        // ONCE, as AuthorizationDenied — like the caller-wide gate above it
        // answers 403 and discloses nothing about any concrete target, so it
        // must not feed the enumeration-probe signal of per-target 404s.
        ApiTokenAuthorization AuthorizeProductCreate(ClaimsPrincipal principal);

        // Decision for creating a chat — the admin-only write that has no boundary to
        // authorize against (the chat does not exist yet). The web UI's AddChat is
        // admin-only — a chat created with an attacker-supplied webhook URL would
        // exfiltrate every alert sent to it — and the mirror keeps that rule: an admin
        // owner with a read-write token is allowed. Unlike the Global-boundary writes it
        // answers Forbidden, never NotFound: a creation has no target whose existence a
        // 403 could disclose. A denial is recorded ONCE, as AuthorizationDenied.
        ApiTokenAuthorization AuthorizeChatCreate(ClaimsPrincipal principal);

        // Decision for CHANGING an existing chat (#1829) — the token-side twin of the web
        // editor's gate (TelegramRoleFilterById, ProductManager). A chat is no boundary
        // of its own: its sight is the read surface's composition (GLOBAL, or bound to at
        // least one folder the owner can see) and its write gate the detail rule (an
        // admin, or the manager of a folder the chat is bound to — so a global chat is
        // admin-only). Unknown ids never reach this: the caller answers them the same 404
        // without an event. NotFound when the chat is outside the owner's sight;
        // Forbidden when it is in sight and the token is read-only or the owner may not
        // change it. Denials are recorded like those of every other write — a refused
        // attempt to re-point a webhook belongs in the audit trail.
        ApiTokenAuthorization AuthorizeChatWrite(ClaimsPrincipal principal, Guid chatId,
            System.Collections.Generic.IReadOnlyCollection<Guid> chatFolderIds);
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


        public ApiTokenAuthorization AuthorizeRead(ClaimsPrincipal principal, ApiTokenResource resource) =>
            Authorize(principal, write: false, resource);

        public ApiTokenAuthorization AuthorizeWrite(ClaimsPrincipal principal, ApiTokenResource resource) =>
            Authorize(principal, write: true, resource);

        public bool IsVisible(ClaimsPrincipal principal, ApiTokenResource resource) =>
            TryResolveCaller(principal, out var owner, out _) &&
            TryResolveBoundary(resource, out var boundary) &&
            OwnerCanSee(owner, boundary);

        public bool CanSeeAnyBoundary(ClaimsPrincipal principal)
        {
            var allowed = TryResolveCaller(principal, out var owner, out _) && OwnerSeesAnyBoundary(owner);

            if (!allowed)
                Record(principal, write: false, ApiTokenResource.GlobalScope, ApiTokenAuthorization.Forbidden);

            return allowed;
        }

        public bool CanSeeChatDetail(ClaimsPrincipal principal, System.Collections.Generic.IReadOnlyCollection<Guid> chatFolderIds) =>
            TryResolveCaller(principal, out var owner, out _)
            && (owner.IsAdmin || chatFolderIds.Any(owner.IsFolderManager));

        public bool CanSeeFolderDetail(ClaimsPrincipal principal, Guid folderId) =>
            TryResolveCaller(principal, out var owner, out _)
            && owner.IsFolderManager(folderId);

        public ApiTokenAuthorization AuthorizeProductCreate(ClaimsPrincipal principal)
        {
            // The write-side rule of the token mirror minus the boundary half:
            // a live caller with a read-write token. The read-write arm is
            // defense-in-depth — the management policy already rejects
            // read-only tokens on unsafe methods — so every transport (REST,
            // MCP) gets the same decision from this one place.
            if (TryResolveCaller(principal, out _, out var readOnly) && !readOnly)
                return ApiTokenAuthorization.Allowed;

            Record(principal, write: true, ApiTokenResource.GlobalScope, ApiTokenAuthorization.Forbidden);
            return ApiTokenAuthorization.Forbidden;
        }

        public ApiTokenAuthorization AuthorizeChatCreate(ClaimsPrincipal principal)
        {
            if (TryResolveCaller(principal, out var owner, out var readOnly) && owner.IsAdmin && !readOnly)
                return ApiTokenAuthorization.Allowed;

            Record(principal, write: true, ApiTokenResource.GlobalScope, ApiTokenAuthorization.Forbidden);
            return ApiTokenAuthorization.Forbidden;
        }

        public ApiTokenAuthorization AuthorizeChatWrite(ClaimsPrincipal principal, Guid chatId,
            System.Collections.Generic.IReadOnlyCollection<Guid> chatFolderIds)
        {
            var resource = ApiTokenResource.Chat(chatId);

            if (!TryResolveCaller(principal, out var owner, out var readOnly))
            {
                Record(principal, write: true, resource, ApiTokenAuthorization.NotFound);
                return ApiTokenAuthorization.NotFound;
            }

            var folders = chatFolderIds ?? [];

            // The read surface's caller-wide gate, in the write's terms. An owner who sees
            // no boundary sees no chat at all, a global one included, and can never pass the
            // change gate below. Forbidden for a global chat would tell it apart from an
            // unknown id, which the write answers 404 before asking here: for such an owner
            // every chat is the same NotFound.
            if (!OwnerSeesAnyBoundary(owner))
            {
                Record(principal, write: true, resource, ApiTokenAuthorization.NotFound);
                return ApiTokenAuthorization.NotFound;
            }

            // The chat sight rule (ChatsReadService): global, or bound to at least one
            // folder the owner can see. IsVisible is the silent sight half — only the
            // decision below is recorded.
            if (folders.Count > 0 && !folders.Any(folderId => IsVisible(principal, ApiTokenResource.Folder(folderId))))
            {
                Record(principal, write: true, resource, ApiTokenAuthorization.NotFound);
                return ApiTokenAuthorization.NotFound;
            }

            // 403: in sight, but a read-only token or an owner who is neither an admin
            // nor the manager of a bound folder.
            if (readOnly || !(owner.IsAdmin || folders.Any(owner.IsFolderManager)))
            {
                Record(principal, write: true, resource, ApiTokenAuthorization.Forbidden);
                return ApiTokenAuthorization.Forbidden;
            }

            return ApiTokenAuthorization.Allowed;
        }

        private ApiTokenAuthorization Authorize(ClaimsPrincipal principal, bool write, ApiTokenResource resource)
        {
            if (!TryResolveCaller(principal, out var owner, out var readOnly))
            {
                Record(principal, write, resource, ApiTokenAuthorization.NotFound);
                return ApiTokenAuthorization.NotFound;
            }

            if (!TryResolveBoundary(resource, out var boundary))
            {
                Record(principal, write, resource, ApiTokenAuthorization.NotFound);
                return ApiTokenAuthorization.NotFound;
            }

            // 404 first: absent or invisible to the owner — indistinguishable, so
            // callers cannot enumerate resources.
            if (!OwnerCanSee(owner, boundary))
            {
                Record(principal, write, resource, ApiTokenAuthorization.NotFound);
                return ApiTokenAuthorization.NotFound;
            }

            // 403: the target is known and in sight, but this is a write and either the
            // owner currently cannot perform writes there (Viewer role) or the token is
            // a read-only credential.
            if (write && (readOnly || !OwnerCanWrite(owner, boundary)))
            {
                Record(principal, write, resource, ApiTokenAuthorization.Forbidden);
                return ApiTokenAuthorization.Forbidden;
            }

            return ApiTokenAuthorization.Allowed;
        }

        // Denials reach the append-only security-event sink with the safe identifiers the
        // design names: token id, subject id, the access mode, a safe target id — and
        // with the decision preserved: 404 denials (invisible targets, the
        // enumeration-probe signal) are AuthorizationNotFound, 403 write denials are
        // AuthorizationDenied. Allowed decisions are not per-request events.
        private void Record(ClaimsPrincipal principal, bool write, ApiTokenResource resource,
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
                tokenId, ownerId, write ? "write" : "read",
                TargetId: $"{resource.Kind}:{resource.Id}"));
        }


        private bool TryResolveCaller(ClaimsPrincipal principal, out User owner, out bool readOnly)
        {
            owner = null;
            readOnly = false;

            // The principal shape is enforced upstream by the management policy; anything
            // else fails closed rather than throwing.
            var ownerClaim = principal?.FindFirst(HsmApiTokenClaims.OwnerUserId);
            var tokenClaim = principal?.FindFirst(HsmApiTokenClaims.TokenId);
            if (ownerClaim is null || tokenClaim is null ||
                !Guid.TryParse(ownerClaim.Value, out var ownerId))
                return false;

            owner = _users[ownerId];

            // Liveness re-check through the manager's sanctioned predicate: a token
            // revoked or generation-invalidated between authentication and this
            // authorization fails closed here, not on the next request.
            if (!_tokens.IsTokenLive(tokenClaim.Value))
                return false;

            var token = _tokens.GetToken(tokenClaim.Value);

            if (owner is null || token is null)
                return false;

            readOnly = token.ReadOnly;
            return true;
        }

        // Current authorization anchor of a target, resolved from the live hierarchy:
        // sensor -> its product, product -> itself plus its current folder.
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
                ApiTokenResourceKind.Global => false, // global resources are admin-only
                ApiTokenResourceKind.Product => owner.IsUserProduct(boundary.Id),
                ApiTokenResourceKind.Folder => owner.IsFolderAvailable(boundary.Id),
                _ => false,
            };

        // Owner write capability: writes need the Manager role at the boundary (admins
        // write everywhere they can see). No folder fallback for products, for the same
        // materialisation reason as OwnerCanSee — the owner side never exceeds what the
        // app's own IsManager check grants.
        private static bool OwnerCanWrite(User owner, AuthorizationBoundary boundary) =>
            owner.IsAdmin || boundary.Kind switch
            {
                ApiTokenResourceKind.Product => owner.IsManager(boundary.Id),
                ApiTokenResourceKind.Folder => owner.IsFolderManager(boundary.Id),
                _ => false, // writes at the global boundary are admin-only
            };

        // The caller-wide gate's owner side: an admin sees everything; any other owner
        // needs a role that currently RESOLVES — a stale entry pointing at a deleted
        // product/folder grants nothing, exactly like TryResolveBoundary fails closed on
        // deleted ids. A user with no resolvable role sees nothing anywhere, so no
        // global resource can disclose anything to them.
        private bool OwnerSeesAnyBoundary(User owner) =>
            owner.IsAdmin ||
            owner.ProductsRoles.Any(role => _cache.TryGetProduct(role.Item1, out _)) ||
            owner.FoldersRoles.Any(role => _folders.TryGetValue(role.Key, out _));

        // Text forms of the ids are precomputed once per boundary.
        private sealed record AuthorizationBoundary(ApiTokenResourceKind Kind, Guid Id, Guid? FolderId);
    }
}
