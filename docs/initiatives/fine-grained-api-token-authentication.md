# Initiative: Fine-grained API token authentication

> Owner: server | Last reviewed: 2026-08-25 | Status: Draft for implementation review | Canonical: no

## Problem

HSM has cookie authentication for its web UI and collector access keys for ingestion, but no durable fine-grained credential for management automation. This standalone initiative defines that missing authentication foundation.

## Goals

Add a single, self-hosted API-token authentication mechanism that can be used by Administrator, Manager, and Client/Viewer accounts, scheduled scripts, resident services, CLI clients, MCP tools, and AI agents.

An authenticated user creates a named token once, copies it to a protected environment or secret store, and uses it as an HTTP bearer credential without repeating the interactive HSM login. HSM must never persist the recoverable token value.

The implementation must support deliberate privilege reduction. A highly privileged user must be able to issue a narrowly restricted token, for example an Administrator creating a read-only token for monitoring. A token can reduce its owner's access but can never increase it.

## Core Authorization Invariant

For every request, effective access is calculated from current state:

```text
effective permissions
    = current permissions of token owner
    ∩ permissions explicitly granted to token
    ∩ permissions valid for the requested operation

effective resources
    = resources currently accessible to token owner
    ∩ resources explicitly granted to token
    ∩ target resource and its applicable hierarchy boundary
```

Consequences:

- An Administrator may create a token with read-only permissions.
- A Manager may create a token limited to one Product even if the Manager can access several Products.
- A Client/Viewer token remains read-only even if a write permission is submitted through a forged request.
- Lowering or removing the owner's role/resource access immediately lowers all of that owner's tokens.
- Disabling or deleting the owner immediately invalidates all of that owner's tokens.
- A token's permissions or resource boundary may be reduced in place.
- Increasing an existing token's permissions or resources is not allowed. The user must create or rotate to a new token and see the new secret once.
- No token may grant another token or account more access unless a separate future permission and threat review explicitly introduces that capability.

The invariant must be enforced in the domain/service layer and authorization policies, not only in the web UI.

## Proposed Direction

### In scope

- Create, list, inspect metadata, restrict, rotate, and revoke personal API tokens.
- One authentication scheme for all existing human role levels.
- Opaque high-entropy bearer-token generation and validation.
- Current-owner, token-permission, and resource-boundary intersection on every request.
- Token metadata persistence in the existing HSM database stack.
- Authentication integration for the new versioned management API.
- Audit records and secret-safe diagnostics.
- Environment-variable usage documentation for unattended clients.
- Unit, integration, authorization-matrix, persistence, and security regression tests.

## Non-Goals

- Building a general OAuth/OIDC authorization server.
- JWT access tokens.
- Replacing cookie authentication for the web UI.
- Replacing collector `ClientName`/access-key authentication.
- Accepting API tokens on ordinary MVC/Razor browser routes.
- HMAC signing of every HTTP request.
- Service-account administration unless it is explicitly pulled into this task after product review. The same token model must be compatible with service accounts later.
- Implementing all management API resources. This task provides the authentication and authorization foundation used by those APIs.

## User Experience

### Token creation

From the authenticated HSM web UI, the user chooses **API tokens → Create token** and provides:

- Name, required, human-readable, unique per owner if practical.
- Optional description/purpose.
- Expiration: recommended presets plus explicit `No expiration` with a warning.
- Permissions selected from the subset currently available to the owner.
- Resource boundary selected from resources currently available to the owner.

The server returns the full token exactly once. The UI must state that it cannot be recovered and must be stored like a password.

Example:

```text
hsm_pat_v1_q4n6YcGJvVf43eP6_4fA0w.XsA8QXjzgV2P-fW8h2iZ7jY9I4ky7pTqt8oH4mzQz7A
```

The exact separators may change during implementation, but the format must remain versioned, parseable without a database scan, URL/header safe, and unambiguous.

### Token usage

```http
Authorization: Bearer hsm_pat_v1_<token-id>.<secret>
```

Example environment configuration:

```env
HSM_URL=https://hsm.example:44333
HSM_TOKEN=hsm_pat_v1_<token-id>.<secret>
```

The token must never be accepted in query parameters or URLs.

### Token listing

List and detail screens return metadata only:

- Token ID and safe display suffix.
- Name and description.
- Owner.
- Granted permissions and resource boundary.
- Created, expires, last used, rotated, and revoked timestamps.
- Creator/initiator if an administrator created it for another subject in a later workflow.
- Status: active, expired, revoked, or owner disabled.

The secret and verifier are never returned.

### Restriction, rotation, and revocation

- **Restrict:** permissions/resources may only be removed; expiration may be shortened. No new secret is produced.
- **Expand:** forbidden on an existing token. Create a replacement token.
- **Rotate:** create a new secret/token record with the selected allowed permissions. Default behavior revokes the previous token immediately. A bounded grace period may be added only if product requirements justify it and audit records identify both records.
- **Revoke:** marks the token revoked immediately and idempotently.

## Cryptographic Design

### Token material

- Token ID: at least 128 random bits, Base64URL encoded. It is a public lookup key, not a secret.
- Token secret: 256 random bits generated by `RandomNumberGenerator.GetBytes(32)`, Base64URL encoded without padding.
- Token prefix/version: `hsm_pat_v1_`.
- Randomness must come only from `System.Security.Cryptography.RandomNumberGenerator`.
- Do not use `Random`, timestamps, usernames, counters, hashes of user data, or a GUID as the only secret material.

### Stored verifier

HSM stores a keyed verifier, never plaintext or reversibly encrypted token material:

```text
verifier = HMAC-SHA-256(
    serverPepper,
    version || tokenIdBytes || tokenSecretBytes
)
```

Requirements:

- `serverPepper` is a separate 256-bit or stronger deployment secret.
- Pepper is not stored in the token database.
- Pepper is loaded from protected server configuration or a deployment secret facility.
- Token records carry `PepperKeyId` to permit controlled future key rotation.
- Losing a pepper invalidates tokens using it; backup/restore documentation must address that dependency.
- Verification uses `CryptographicOperations.FixedTimeEquals`.
- Temporary byte buffers containing token secrets should be cleared with `CryptographicOperations.ZeroMemory` when practical.
- Token parsing must reject malformed/oversized inputs before database access or expensive work.

A fast HMAC verifier is appropriate because the server generates a uniformly random 256-bit secret. Password hashing algorithms such as Argon2id/PBKDF2/bcrypt are required for low-entropy human passwords, not for server-generated 256-bit credentials. Users must not be allowed to choose token secrets.

### Why opaque tokens

Opaque tokens are selected instead of JWTs because HSM requires:

- Immediate revocation.
- Immediate reaction to owner role/resource changes.
- Server-controlled token metadata and last-used tracking.
- No authorization claims frozen into a long-lived client-visible credential.

Every authenticated request therefore performs a token-record lookup and loads current owner authorization state. Do not add a long-lived validation cache. If caching becomes necessary, it must have explicit invalidation on token/owner/role/resource changes and a security-reviewed maximum stale interval.

## Persistence Model

Introduce a compatibility-versioned API token entity in the existing database/access-manager layer. Exact project placement should follow current user/access-key storage patterns.

Required fields:

```text
Id                    public random token ID / primary lookup key
Version               token format and verifier version
Verifier              32-byte HMAC-SHA-256 result
PepperKeyId           verifier key identifier
OwnerUserId           current owning user/subject
Name                  human-readable token name
Description           optional purpose
Permissions           normalized allow-list
ResourceBoundary      normalized allowed resource identifiers
CreatedAtUtc
CreatedBy             audit initiator
ExpiresAtUtc           nullable only when no-expiration is explicitly selected
LastUsedAtUtc          nullable, operational metadata
RotatedFromId          nullable
RevokedAtUtc           nullable
RevokedBy              nullable
RevocationReason       nullable, sanitized
```

Persistence rules:

- Never serialize plaintext token values.
- Never reuse a token ID or secret.
- Creation of metadata and verifier must be atomic from the caller's perspective.
- A failure after persistence but before the one-time response must revoke/delete the unusable record safely; do not attempt to expose it later.
- Revocation is idempotent.
- Last-used updates must not create excessive synchronous database writes. Use an established coalescing/background pattern with bounded loss acceptable only for this non-security-critical timestamp.
- Unknown permission values must fail closed during deserialization/authorization.
- Storage migration and backup/restore compatibility must be documented and tested.

## Permission Model

Create a canonical permission catalog owned by the management API/domain layer. Initial names should be resource/action oriented, for example:

```text
products:read
products:write
sensors:read
sensors:write
history:read
alerts:read
alerts:write
dashboards:read
dashboards:write
notifications:read
notifications:write
users:read
users:manage
access-keys:read
access-keys:manage
server-settings:read
server-settings:manage
```

This list is illustrative until the API capability inventory is approved. Requirements:

- Each API operation maps to one or more canonical permissions.
- Each permission declares the minimum current owner role/condition that can exercise it.
- Token permission selection is an allow-list; absence means denied.
- `*`, `admin`, controller-name permissions, and implicit write-through-read permissions are forbidden in the initial implementation.
- Read and write are separate.
- Destructive, credential, user, backup/restore, and global-settings actions require separate high-risk permissions rather than generic `write`.
- Permission checks do not replace object/resource authorization.

### Mandatory privilege-reduction examples

| Owner | Token grant | Expected result |
|---|---|---|
| Administrator | `products:read`, `sensors:read`, `history:read`, all visible products | Read-only monitoring token; every mutation returns 403. |
| Administrator | `alerts:read`, one Product | May read alerts only inside that Product; cannot read unrelated sensor history or change alerts. |
| Manager | Read/write alerts for one permitted Product | Allowed only while the Manager retains both the role permission and Product access. |
| Client/Viewer | Forged request containing `alerts:write` | Creation fails or strips the impossible grant; runtime mutation is denied in all cases. Prefer failing the creation request with a clear validation error. |
| Any owner later downgraded | Previously broader token | Effective access is reduced immediately without changing the token record. |

## Resource Scope

Define a normalized boundary model after the capability inventory confirms HSM hierarchy semantics. The initial implementation should prefer stable IDs rather than paths/names.

Requirements:

- A token may select all resources currently visible to the owner or an explicit subset.
- Resource authorization is evaluated on the target object for every request.
- Parent/child inheritance must be explicit and tested, including moved Products/Folders.
- Moving a resource must not accidentally preserve access derived from an old parent.
- Deleted resource IDs fail closed.
- A token cannot broaden its boundary through a request body, query parameter, or object reference.
- List endpoints filter results; detail/mutation endpoints return 404 or 403 according to one documented anti-enumeration policy.

## ASP.NET Core Architecture

### Authentication scheme

Add a dedicated authentication scheme, for example `HsmApiToken`, implemented with an ASP.NET Core authentication handler/service boundary.

Responsibilities:

1. Read only the `Authorization` header.
2. Ignore the request when another supported scheme is clearly in use; reject malformed HSM bearer credentials predictably.
3. Parse token prefix, version, ID, and secret with strict length/character limits.
4. Load token metadata by ID.
5. Verify active/expiry/revocation state and owner state.
6. Compute verifier and compare in constant time.
7. Load current owner role and resource access.
8. Build a `ClaimsPrincipal` containing stable subject/token identifiers, not copied broad authorization claims that could bypass the intersection service.
9. Return a generic authentication failure without revealing whether token ID, secret, owner, expiration, or revocation caused it.

The handler should orchestrate authentication only. Token lifecycle, verification, permission intersection, resource authorization, and auditing belong in dedicated services/domain components, not controllers.

### Scheme isolation

- Existing cookie authentication remains the web UI scheme.
- Existing collector access-key validation remains unchanged.
- New `/api/v1` management endpoints explicitly require the API-token scheme, or an intentional policy that supports cookie access for interactive Swagger/testing without weakening token checks.
- API tokens must not authenticate MVC/Razor pages.
- Token-management creation UI uses the authenticated cookie session and existing anti-forgery protection.
- API-token-based creation of more tokens is out of scope initially.

### Authorization services

Introduce explicit abstractions whose final names follow repository conventions:

- Token lifecycle service: create/list/restrict/rotate/revoke.
- Token verifier/authentication service.
- Permission catalog and effective-permission evaluator.
- Resource-authorization evaluator.
- Audit writer.

Controllers map HTTP requests/responses and call these services. No cryptography, persistence formatting, or permission calculation belongs in controllers.

### HTTP semantics

- Missing/invalid/revoked/expired token: `401 Unauthorized` with a generic bearer challenge.
- Valid token lacking permission or resource access: `403 Forbidden`, except where the approved anti-enumeration policy requires `404`.
- Impossible privilege grant or attempted expansion: `400 Bad Request` or `403 Forbidden`, selected consistently and documented.
- Revoking an already revoked token: idempotent success.
- Secret is returned only by successful create/rotate responses and is marked `Cache-Control: no-store`.
- Token-management responses never echo an incoming bearer token.

## Proposed Token Management Endpoints

Exact routes must follow the versioned management API convention:

```text
POST   /api/v1/api-tokens                 create and reveal once
GET    /api/v1/api-tokens                 list own token metadata
GET    /api/v1/api-tokens/{id}            get own token metadata
PATCH  /api/v1/api-tokens/{id}/restrict   remove permissions/resources or shorten expiry
POST   /api/v1/api-tokens/{id}/rotate     issue replacement and reveal once
DELETE /api/v1/api-tokens/{id}            revoke idempotently
```

Administrative token management for another user, if approved, must use separate explicit permissions and must never reveal or recover an existing secret.

## Secret Handling and Operational Security

- HTTPS is mandatory; validate server certificates.
- Never accept tokens in URLs, query strings, cookies, or request bodies for ordinary authentication.
- Redact `hsm_pat_...` patterns in application/request logs and exception serialization.
- Do not include token values in audit events, telemetry, validation errors, tracing baggage, crash reports, UI analytics, shell examples with real values, or OpenAPI examples.
- Do not put tokens in process command-line arguments.
- Document `.env` as a supported simple profile only when the file is outside source control and protected by OS permissions.
- Recommend OS/deployment secret stores for stronger profiles.
- Add rate limiting/backoff for repeated invalid token attempts without creating an attacker-controlled unbounded cache.
- Record successful and rejected token use with safe identifiers, correlation ID, source information already permitted by HSM privacy policy, and result.
- Provide a server-side emergency operation to revoke all tokens for a user and, if necessary, all API tokens.
- Pepper configuration must fail safely. Do not silently generate an ephemeral pepper on every startup because that would invalidate persisted tokens unpredictably.

## Audit Events

At minimum record:

- Token created, with owner, creator, name, permissions, resources, expiration, and token ID.
- Token restricted, with safe before/after metadata.
- Token rotated, linking old and new IDs.
- Token revoked, with initiator and reason.
- Token authentication succeeded, sampled/coalesced if necessary for volume but preserving security usefulness.
- Token authentication failed, rate-limited/coalesced without exposing secrets.
- Authorization denied, with token ID, subject ID, required permission, safe target identifier, and correlation ID.

Audit storage must never contain secret or verifier values.

## Configuration

Introduce an explicit configuration section, names illustrative:

```text
ApiTokens.Enabled
ApiTokens.PepperKeys.<keyId>
ApiTokens.ActivePepperKeyId
ApiTokens.DefaultLifetime
ApiTokens.AllowNoExpiration
ApiTokens.MaxTokensPerUser
ApiTokens.InvalidAttemptRateLimit
```

Requirements:

- Safe default is disabled until a valid pepper is configured, unless installation tooling generates and persists one securely exactly once.
- Configuration validation occurs at startup with actionable errors.
- Pepper values must be treated as secrets by configuration diagnostics.
- Limits prevent unbounded token records and abuse.

## Verification

### Cryptographic/token format tests

- Generated IDs and secrets have required byte lengths and valid Base64URL encoding.
- Large generation sample contains no duplicates.
- Plaintext token never appears in serialized persistence entities.
- Correct token verifies; any changed ID/secret/version fails.
- Comparison uses the dedicated verifier boundary and malformed inputs fail safely.
- Missing/wrong pepper and unknown key ID fail closed.

### Lifecycle tests

- Create returns secret once; subsequent reads return metadata only.
- Revoke is immediate and idempotent.
- Expired token fails.
- Rotation returns a new secret and invalidates the old token according to policy.
- Restriction removes permissions/resources immediately.
- Permission/resource expansion of an existing token fails.
- Disabled/deleted owner invalidates token.
- Persistence survives server restart with the configured pepper.

### Authorization matrix tests

- Administrator full token receives explicitly granted permissions only.
- Administrator read-only token cannot call any mutation endpoint.
- Manager cannot grant or exercise administrator-only permissions.
- Client/Viewer cannot grant or exercise write permissions.
- Owner role downgrade immediately reduces an existing token.
- Owner resource removal immediately reduces an existing token.
- Cross-Product and cross-Folder object access is denied.
- List responses do not leak unauthorized objects.
- Forged permission/resource values in request payloads fail closed.

### HTTP/security tests

- Header authentication succeeds with the documented scheme.
- Token in query/body/cookie is ignored/rejected.
- Missing/invalid/revoked/expired results are indistinguishable to unauthenticated callers.
- 401/403/404 behavior matches the documented policy.
- Create/rotate response has `Cache-Control: no-store`.
- Logs, audit, tracing, exception output, and validation responses contain no full token or verifier.
- Oversized/malformed headers do not cause excessive allocation, exceptions, or database scans.
- Invalid-attempt limiting is bounded and does not block valid users globally.

### Compatibility tests

- Existing cookie login and authorization behavior remain unchanged.
- Existing collector access-key requests remain unchanged.
- Existing MVC/Razor routes do not accept API tokens.
- Database migration and backup/restore behavior are verified.

## Documentation Deliverables

- Update the parent initiative to make fine-grained API tokens the initial authentication decision and OAuth/OIDC optional future work.
- Add canonical behavior documentation under `aicontext/features/server/auth/api-tokens/feature.md` when implementation begins.
- Document token creation, one-time display, restriction, rotation, revocation, and emergency response.
- Document environment-variable use and safer OS secret-store options.
- Add a permission catalog/API operation matrix under `aicontext/features/api/` as management endpoints land.
- Add an ADR explaining opaque tokens, HMAC verifier storage, current-rights intersection, and rejection of long-lived JWT for this use case.

## Work Breakdown

This architecture should be delivered in focused pull requests rather than one large change:

1. **ADR and permission inventory** — approve token/permission/resource semantics and persistence compatibility.
2. **Token domain and persistence** — entity, repository/storage, generation, verifier, lifecycle service, tests.
3. **Authentication scheme and policies** — handler, effective-rights intersection, resource authorization, audit integration, tests.
4. **Token management UI/API** — create/list/restrict/rotate/revoke, one-time secret handling, CSRF, tests.
5. **First read-only management endpoints** — prove Administrator read-only downgrade, Manager/Client behavior, and unattended environment-token journey end to end.

Each PR must update the actual behavior documentation and run focused server/security review. Do not expose broad management mutations until the authorization matrix and negative tests are established.

## Acceptance Criteria

- A user in each current role can create a token only with a subset of the user's current permissions and resources.
- An Administrator can create a read-only monitoring token, and automated tests prove that every covered mutation is denied with that token.
- The server never persists or logs the recoverable token value.
- A copied token authenticates through the HTTP `Authorization: Bearer` header after server restart.
- Revocation, expiration, owner disablement, role downgrade, resource removal, and token restriction affect subsequent requests immediately.
- Existing token permissions/resources cannot be expanded in place.
- Existing cookie and collector authentication remain compatible and isolated.
- All authentication, privilege-reduction, cross-resource denial, storage, rotation, revocation, and secret-redaction tests pass.
- Canonical auth/API documentation and the architecture decision are updated from the implemented behavior.

## Implementation Questions Requiring Review

1. What are the exact existing Administrator/Manager/Viewer rights, and how do they map to the first canonical permission catalog?
2. What is the first stable resource boundary: Environment, Folder subtree, Product, or a typed combination?
3. Should `No expiration` be enabled by default, enabled with explicit confirmation, or controlled by server policy?
4. Is same-user token-name uniqueness useful, or is token ID sufficient?
5. Does initial rotation revoke immediately, or is a short configurable overlap required for unattended deployments?
6. Which existing audit/journal storage should own security events, and what retention is required?
7. How will the server pepper be generated, persisted, backed up, and rotated in Docker and non-Docker installations?
8. Should service accounts be included in the first delivery or follow after personal tokens prove the model?

## Current Behavior and Normative Architecture Corrections

This section is normative and supersedes any earlier wording that conflicts with it.

### Existing authentication and role model

- Cookie is the only registered ASP.NET Core authentication scheme.
- UserProcessorMiddleware runs after authentication and authorization on site port 44333 and replaces HttpContext.User with the stored HSM User selected by Identity.Name.
- BaseController requires HttpContext.User to be an HSM User.
- IsAdmin is a global flag. ProductManager and ProductViewer are per-Product/per-Folder roles. There is no global Client role.
- Ports 44330 and 44333 share the same routing table unless an endpoint explicitly constrains the local port.
- Collector ClientName/access-key authentication remains unchanged.

### Per-resource authorization invariant

Authorization is evaluated for the concrete operation and target resource:

    allowed(operation, resource) =
        ownerCurrentlyAllows(operation, resource)
        AND tokenPermissions contains operation
        AND tokenResourceScope contains resource

ownerCurrentlyAllows uses the current IsAdmin flag or ProductManager/ProductViewer assignment for the target Product/Folder. Owner permissions and resource scope must not be flattened into independent global sets.

An IsAdmin user can issue a read-only token. A ProductManager can issue a token restricted to one managed Product. A ProductViewer token remains read-only. Owner downgrade, owner disablement, resource-role removal, token restriction, revocation, and expiration affect subsequent requests immediately.

### Principal and middleware isolation

The HsmApiToken handler creates a minimal principal containing stable owner ID, token ID, and authentication-scheme identity. It must not expose the unrestricted stored User as the authorization result.

- UserProcessorMiddleware must skip requests authenticated by HsmApiToken.
- Versioned management controllers derive from ControllerBase, not BaseController.
- Management authorization uses dedicated per-resource services and never the legacy CurrentUser cast.
- Any management endpoint entering the legacy BaseController/User path fails closed.
- Tests must prove that a read-only IsAdmin token cannot regain IsAdmin rights after authorization.

### Port isolation

The management API is hosted only on site port 44333. An endpoint filter, middleware guard, or equivalent constraint rejects every management route on sensor port 44330 before controller execution. Tests exercise both ports.

### Token-management and rotation rules

Every token-management operation, including create, list, detail, restrict, rotate, and revoke, requires an interactive cookie-authenticated session with anti-forgery protection in the initial implementation. API tokens cannot manage API tokens.

Rotation preserves or reduces the source token's permissions and resource scope. It can never broaden either. Creating a broader token requires a new cookie-authenticated create operation. Tests must cover a read-only IsAdmin token attempting self-rotation and every other token-management endpoint.

### Resource scope and rate limiting

Resource scope defines which HSM objects the token may access, such as a Product, Folder subtree, or selected Sensors. It is object-level authorization, not throttling.

Rate limits and quotas independently control request frequency or volume. Initial rate limiting may be server-wide or keyed by token ID without adding configurable quota fields to the token record.

### Verification ordering and response policy

After parsing and loading by public token ID, compute and compare the HMAC verifier with FixedTimeEquals before evaluating revocation, expiration, owner, or resource state. Failed authentication uses one generic 401 response shape and normalized observable processing.

A valid token denied an operation on an already visible resource receives 403. Detail or mutation access to an object outside token resource scope receives 404. List endpoints filter inaccessible objects.

### Pepper lifecycle

Pepper rotation is issue-forward only. New tokens use the active pepper. Existing tokens cannot be re-keyed because HSM does not store their plaintext secrets. Retired peppers remain configured until every referencing token expires or is re-issued. Fully retiring a pepper is a user-visible mass re-issue operation. Token records retain PepperKeyId.

### Documentation requirements

- Add the architecture decision under docs/decisions/ using the repository template and update docs/decisions/INDEX.md.
- Add API token, verifier, pepper, permission catalog, and resource scope to aicontext/glossary.md before they become canonical.
- Document the first operation-by-resource matrix for IsAdmin, ProductManager, and ProductViewer.
- Keep this initiative standalone until a consistent control-plane parent initiative is merged.

## Risks

- Principal replacement may silently restore unrestricted owner rights.
- Missing port isolation may expose management routes on the collector-facing port.
- Flattening per-resource roles may allow cross-Product access.
- Rotation may become privilege escalation unless it is cookie-only and non-expanding.
- Long-lived bearer-token disclosure grants access until revocation or expiration.
- Pepper retirement requires coordinated token re-issue.

## Additional Acceptance Criteria

- UserProcessorMiddleware never replaces an HsmApiToken principal.
- Management controllers do not inherit BaseController or consume its CurrentUser.
- Management routes are rejected on port 44330.
- Every token-management route is cookie-only.
- Rotation preserves or reduces the source grant.
- A read-only IsAdmin token cannot call any mutation or token-management route.
- Authorization is tested per resource for IsAdmin, ProductManager, and ProductViewer.
- Secret verification precedes token-state evaluation.
- 401, 403, and 404 follow the policy above.
- Pepper issue-forward rotation and retirement are documented and tested.
