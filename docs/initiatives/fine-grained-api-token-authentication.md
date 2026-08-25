# Initiative: Fine-grained API token authentication

> Owner: server | Last reviewed: 2026-08-25 | Canonical: yes

## Problem

HSM has cookie authentication for its web UI and collector access keys for ingestion, but no durable fine-grained credential for management automation. This standalone initiative defines that missing authentication foundation.

## Goals

Add a single, self-hosted API-token authentication mechanism for IsAdmin users, per-resource ProductManager and ProductViewer users, scheduled scripts, resident services, CLI clients, MCP tools, and AI agents.

An authenticated user creates a named token once, copies it to a protected environment or secret store, and uses it as an HTTP bearer credential without repeating the interactive HSM login. HSM must never persist the recoverable token value.

The implementation must support deliberate privilege reduction. A highly privileged user must be able to issue a narrowly restricted token, for example an IsAdmin user creating a read-only token for monitoring. A token can reduce its owner's grant set. Owner promotion cannot create an operation/resource grant that was not explicitly issued; it may restore effective access within a previously issued grant after a temporary owner downgrade.

## Non-Goals

- Building a general OAuth/OIDC authorization server.
- JWT access tokens.
- Replacing cookie authentication for the web UI.
- Replacing collector `ClientName`/access-key authentication.
- Accepting API tokens on ordinary MVC/Razor browser routes.
- HMAC signing of every HTTP request.
- Unattended self-rotation or self-revocation by an API token in v1. Operators use the cookie-authenticated lifecycle flow and plan an atomic human-in-the-loop cutover.
- Service-account administration unless it is explicitly pulled into this task after product review. The same token model must be compatible with service accounts later.
- Implementing all management API resources. This task provides the authentication and authorization foundation used by those APIs.

## Current Behavior

- Cookie is the only registered ASP.NET Core authentication scheme.
- UserProcessorMiddleware runs after authentication and authorization on the configured SitePort listener (default 44333) and replaces HttpContext.User with the stored HSM User selected by Identity.Name.
- BaseController requires HttpContext.User to be an HSM User.
- IsAdmin is a global flag. ProductManager and ProductViewer are assigned per Product or Folder; there is no global Client role.
- HSM has no owner-disabled/blocked user state; deletion and role/resource removal are the available lifecycle controls.
- Kestrel SitePort and SensorPort are configurable (defaults 44333 and 44330). Both listeners currently share the same routing table, and no general endpoint-local-port constraint exists.
- Collector ClientName/access-key authentication remains unchanged.
- Existing Grafana datasource routes and Swagger UI/OpenAPI (`api/swagger`) are anonymous, have no fallback authorization policy, and are reachable on both listeners; this initiative must not silently change Grafana behavior.
- HSM has no existing `/api/v1` management-route convention; establishing it and coexistence with unversioned and Grafana routes is part of this initiative.

## Proposed Direction

### In scope

- Create, list, inspect metadata, restrict, rotate, and revoke personal API tokens.
- One API-token scheme whose authorization composes with current IsAdmin and per-resource ProductManager/ProductViewer access.
- Opaque high-entropy bearer-token generation and validation.
- Current-owner authorization intersected with explicit operation/boundary grants on every request.
- Token metadata persistence in the existing HSM database stack.
- Authentication integration for the new versioned management API.
- Audit records and secret-safe diagnostics.
- Environment-variable usage documentation for unattended clients.
- Unit, integration, authorization-matrix, persistence, and security regression tests.

## Core Authorization Invariant

Authorization is evaluated for the concrete operation and target resource:

    allowed(operation, resource)
        = ownerCurrentlyAllows(operation, resource)
        AND tokenGrantAllows(operation, currentAuthorizationBoundary(resource))

tokenGrantAllows is evaluated from explicit grant entries that bind an operation to a Product/Folder boundary (or to the explicit global boundary for global operations). Permissions and resource boundaries must not be stored or evaluated as two freely recombinable global sets. This prevents a token that can write Product A and read Product B from acquiring write access to Product B merely because the owner is promoted later.

ownerCurrentlyAllows uses the current IsAdmin flag or ProductManager/ProductViewer assignment for the target Product or Folder. Effective access is recalculated on every request. Owner promotion may satisfy the owner side of the intersection but never creates a token grant that was not explicitly issued.

Consequences:

- An IsAdmin user may create a read-only token.
- A ProductManager may create a token limited to one managed Product.
- A ProductViewer token remains read-only even if a forged request asks for write access.
- Lowering or removing the owner's per-resource role immediately lowers every associated token.
- Deleting the owner immediately invalidates every associated token.
- A token's permissions, resource scope, or expiration may be reduced in place.
- An existing token can never expand its permissions or resource scope, including through rotation.
- Creating a broader token requires an interactive cookie-authenticated create operation.
- No API token may call token-management endpoints in the initial implementation.

The invariant must be enforced in domain services and authorization policies, not only in the web UI.

## User Experience

### Token creation

From the authenticated HSM web UI, the user chooses **API tokens → Create token** and provides:

- Name, required, human-readable, unique per owner if practical.
- Optional description/purpose.
- Expiration: recommended presets plus explicit `No expiration` with a warning.
- Explicit operation/resource grants selected from combinations currently available to the owner.
- Product/Folder boundary selected for each resource-scoped operation; global operations use an explicit global boundary.

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

- Token ID.
- Name and description.
- Owner.
- Granted operation/resource pairs, rendered as permissions grouped by Product/Folder or global boundary.
- Created, expires, last used, rotated, and revoked timestamps.
- Creator/initiator if an administrator created it for another subject in a later workflow.
- Status: active, expired, revoked, or owner missing.

The secret and verifier are never returned.

### Restriction, rotation, and revocation

- **Restrict:** operation/resource grants may only be removed; expiration may be shortened. No new secret is produced.
- **Expand:** forbidden on an existing token. Create a replacement token through an interactive cookie-authenticated flow.
- **Rotate:** create a new secret/token record with the same or a strict subset of the source operation/resource grants and an `ExpiresAtUtc` no later than the source value. A finite expiry cannot become `No expiration`. In v1 the previous token is revoked in the same atomic operation, the replacement takes its slot for `MaxTokensPerUser`, and overlap/grace periods are out of scope.
- **Revoke:** marks the token revoked immediately and idempotently.

## Cryptographic Design

### Token material

- Token ID: exactly 128 random bits, Base64URL encoded. It is a public lookup key, not a secret.
- Token secret: 256 random bits generated by `RandomNumberGenerator.GetBytes(32)`, Base64URL encoded without padding.
- Token prefix/version: `hsm_pat_v1_`.
- Randomness must come only from `System.Security.Cryptography.RandomNumberGenerator`.
- Do not use `Random`, timestamps, usernames, counters, hashes of user data, or a GUID as the only secret material.

### Stored verifier

HSM stores a keyed verifier, never plaintext or reversibly encrypted token material:

```text
verifier = HMAC-SHA-256(
    serverPepper,
    ASCII("HSM-API-TOKEN") || 0x00 || versionByte[1] || tokenIdBytes[16] || tokenSecretBytes[32]
)
```

Requirements:

- `serverPepper` is a separate 256-bit or stronger deployment secret.
- Pepper is not stored in the token database.
- Pepper is loaded from protected server configuration or a deployment secret facility.
- Token records carry PepperKeyId for issue-forward rotation. New tokens use the active pepper; existing tokens remain bound to the original pepper.
- Retired peppers remain configured until every referencing token expires or is re-issued. Non-expiring tokens always require explicit re-issue before their pepper can retire. Fully retiring one is a mass token re-issue operation. Losing a pepper invalidates its tokens.
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

Every authenticated request resolves token and current-owner state from an authoritative store. A write-through in-memory token store following the existing `ConcurrentStorage` pattern is allowed when create/restrict/rotate/revoke, owner deletion, role changes, and resource moves synchronously update or invalidate it. Secondary stale caches without complete invalidation are forbidden; any bounded stale interval requires a separate security decision.

## Persistence Model

Add a versioned `ApiTokenEntity` collection/store under `HSMDatabase.AccessManager`, accessed only through a dedicated token manager/lifecycle service and mirrored by an authoritative write-through in-memory store using existing AccessManager/`ConcurrentStorage` patterns. LevelDB compatibility uses entity versioning and tolerant fail-closed deserialization rather than a relational migration framework.

Required fields:

```text
Id                    public random token ID / primary lookup key
VersionByte           exactly 1 byte; 0x01 for hsm_pat_v1 token/verifier format
Verifier              32-byte HMAC-SHA-256 result
PepperKeyId           verifier key identifier
OwnerUserId           current owning user/subject
Name                  human-readable token name
Description           optional purpose
Grants                normalized operation + Product/Folder/global-boundary pairs
CreatedAtUtc
CreatedBy             audit initiator
RestrictedAtUtc        nullable
RestrictedBy           nullable audit initiator
ExpiresAtUtc           nullable only when no-expiration is explicitly selected
LastUsedAtUtc          nullable, operational metadata
RotatedAtUtc           nullable
RotatedFromId          nullable
RevokedAtUtc           nullable
RevokedBy              nullable
RevocationReason       nullable, sanitized
```

Persistence rules:

- Never serialize plaintext token values.
- Never reuse a token ID or secret. Creation is a conditional insert: if the public ID already exists, fail closed and generate a completely new ID/secret pair; a blind LevelDB overwrite is forbidden.
- Creation of metadata and verifier must be atomic from the caller's perspective.
- A failure after persistence but before the one-time response must revoke/delete the unusable record safely; do not attempt to expose it later.
- Revocation is idempotent.
- Revoked and expired records do not count toward `MaxTokensPerUser`; they are retained for `TokenRecordRetention` and then removed by bounded maintenance while lifecycle/security audit records follow their own retention policies.
- Last-used updates must not create excessive synchronous database writes. Use an established coalescing/background pattern with bounded loss acceptable only for this non-security-critical timestamp.
- Unknown operations, boundary kinds, or resource identifiers must fail closed during deserialization/authorization.
- The lifecycle service canonicalizes grants and rejects duplicate operation/boundary pairs before the atomic LevelDB write; do not assume relational uniqueness constraints.
- Owner deletion synchronously revokes/removes associated token records when possible. Startup/background maintenance reaps orphan token records left by interrupted deletion; orphan records always fail authentication and do not count toward `MaxTokensPerUser`.
- Entity-version upgrade, tolerant fail-closed deserialization, and existing LevelDB backup/restore compatibility must be documented and tested.

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
access-keys:read
server-settings:read
```

This list is illustrative until the API capability inventory is approved. Requirements:

- Each API operation maps to one or more canonical permissions.
- Each permission declares the minimum current owner role/condition that can exercise it.
- Token permission selection is an allow-list; absence means denied.
- `*`, `admin`, controller-name permissions, and implicit write-through-read permissions are forbidden in the initial implementation.
- Read and write are separate.
- Destructive, credential, user-role, backup/restore, access-key creation, pepper, and global-settings mutations are non-grantable and cookie-only in v1. Names such as `users:manage`, `access-keys:manage`, and `server-settings:manage` must not exist in the v1 token permission catalog.
- Permission checks do not replace object/resource authorization.

### Mandatory privilege-reduction examples

| Owner | Token grant | Expected result |
|---|---|---|
| IsAdmin | Read grants for `products`, `sensors`, and `history` on selected Product/Folder boundaries | Read-only monitoring token; every mutation returns 403. |
| IsAdmin | `alerts:read` bound to one Product | May read alerts only inside that Product; cannot read unrelated sensor history or change alerts. |
| ProductManager on Product A | Read/write alert grants bound to Product A | Allowed only while the owner retains ProductManager authorization for Product A. |
| ProductViewer on Product A | Forged `alerts:write` grant for Product A | Creation fails with a clear validation error; runtime mutation is denied in all cases. |
| Any owner later downgraded | Previously broader token | Effective access is reduced immediately without changing the token record. |

## Resource Scope

In v1, resource-scoped grants bind operations to stable Product or Folder IDs. A Sensor is an authorization target, not an independently selectable scope ID: it inherits the current Product/Folder boundary resolved from the live hierarchy. Global operations use a distinct explicit global boundary.

Requirements:

- `All available boundaries` is a UI convenience only: creation expands it to concrete Product/Folder boundary IDs, and no wildcard boundary is persisted. Each selected operation is bound to each concrete boundary in the persisted grant.
- An explicit Folder-boundary grant intentionally covers current and future descendants of that Folder; this is the sole accepted dynamic-membership case. Newly assigned owner resources outside an already granted Folder never enter an existing token.
- Resource authorization is evaluated on the target object and its current hierarchy for every request.
- Folder inheritance must be explicit and tested, including moved Products/Folders/Sensors.
- Moving a Product, Folder, or Sensor immediately recomputes its current boundary; access derived from the old parent must not survive the move, and access in the new parent exists only when an explicit token grant covers that boundary.
- Deleted resource IDs fail closed.
- A token cannot broaden its boundary through a request body, query parameter, or object reference.
- List endpoints filter results; detail/mutation endpoints return 404 or 403 according to one documented anti-enumeration policy.

## ASP.NET Core Architecture

### Authentication scheme

Add a dedicated authentication scheme, for example `HsmApiToken`, implemented with an ASP.NET Core authentication handler/service boundary.

Responsibilities:

1. Read only the Authorization header.
2. Ignore another clearly selected scheme; reject malformed HSM bearer credentials predictably.
3. Parse prefix, version, ID, and secret with strict limits.
4. Load token metadata by public ID and resolve `PepperKeyId` to its configured key. On a missing ID or unknown key ID, select fixed dummy record/key material for the parsed format without exposing which lookup failed.
5. Compute the real or dummy HMAC and compare with `FixedTimeEquals` before evaluating revocation, expiration, or owner/resource state. Complete the dummy path before returning the same generic failure.
6. After successful secret verification, check revocation, expiration, and owner existence.
7. Load current IsAdmin and per-Product/per-Folder roles.
8. Build a minimal ClaimsPrincipal with owner ID, token ID, and authentication-scheme identity.
9. Return one generic authentication failure without revealing which validation failed.

The handler never exposes the unrestricted stored User as the token authorization result.
The handler should orchestrate authentication only. Token lifecycle, verification, permission intersection, resource authorization, and auditing belong in dedicated services/domain components, not controllers.

### Scheme and port isolation

- Cookie authentication remains the web UI scheme.
- Every data-management `/api/v1/*` endpoint except `/api/v1/api-tokens` explicitly selects a policy restricted to the `HsmApiToken` scheme and rejects a cookie-only principal. Plain `[Authorize]` with the default cookie scheme is forbidden on these controllers.
- `/api/v1/api-tokens` is the sole v1 cookie-only route family and never falls back to `HsmApiToken`.
- Collector access-key validation remains unchanged.
- The management API is hosted only on the configured Kestrel SitePort listener (default 44333).
- Apply a fail-closed `/api/v1` endpoint convention/metadata policy to the whole management area: endpoints are allow-listed only on the SitePort listener and rejected everywhere else, including SensorPort, before controller execution. A newly added `/api/v1` route without the required metadata/policy is unavailable by default.
- Populate one immutable `HsmListenerBindings` registry while configuring Kestrel and inject that same registry into the area guard and authentication middleware. Do not independently capture mutable config values; configuration changes take effect only when listeners and the registry are rebuilt on restart.
- `UserProcessorMiddleware.cs` must short-circuit for the `HsmApiToken` authentication type and must never replace or null an authenticated token principal. The token handler must not set `Identity.Name` to a stored user login.
- Management controllers derive from `ControllerBase`, not `BaseController`.
- API tokens do not authenticate MVC/Razor pages.
- Every token-management endpoint requires an interactive cookie session. State-changing methods require anti-forgery protection; GET list/detail routes must be side-effect-free.
- API tokens cannot create, list, inspect, restrict, rotate, or revoke tokens.
- Tests exercise both listeners, fail-closed behavior for a new `/api/v1` route missing metadata, the principal-replacement path, cookie rejection on data-management routes, and API-token rejection on lifecycle routes.
- Management OpenAPI documents and Swagger UI are unavailable on SensorPort. On SitePort they declare the `HsmApiToken` bearer security definition/requirements without real token examples and accurately distinguish cookie-only lifecycle routes.

### Authorization services

Introduce explicit abstractions whose final names follow repository conventions:

- Token lifecycle service: create/list/restrict/rotate/revoke.
- Token verifier/authentication service.
- Permission catalog and effective-permission evaluator.
- Resource-authorization evaluator.
- Lifecycle-audit adapter that uses `IJournalService`, `InitiatorInfo`, and table-of-changes patterns for created/restricted/rotated/revoked events tied to a stable token entity GUID.
- Separate append-only security-event sink for per-request authentication success/failure and authorization denial. It must support collision-free event IDs, volume controls, global query, and retention; per-request events must not use the entity-keyed journal.

Controllers map HTTP requests/responses and call these services. No cryptography, persistence formatting, or permission calculation belongs in controllers.

### HTTP semantics

- Missing/invalid/revoked/expired token: `401 Unauthorized` with a generic bearer challenge.
- A valid token denied an operation on a resource still visible to its current owner receives 403. If the owner no longer has visibility to the target, or the target is outside the token grant, detail/mutation returns 404. List endpoints filter both owner-invisible and token-out-of-scope objects.
- Malformed grant syntax, unknown operation/boundary kind, or structurally impossible grant shape returns `400 Bad Request`. A well-formed grant that the current owner is not authorized to issue, or an attempted expansion of an existing token, returns `403 Forbidden`.
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

All endpoints in this section are cookie-session-only initially. Later administrative management for another user requires a separate threat review.

## Secret Handling and Operational Security

- HTTPS is mandatory; validate server certificates.
- Never accept tokens in URLs, query strings, cookies, or request bodies for ordinary authentication.
- Pepper values and key-management operations are loaded only from the deployment secret facility/environment. They are excluded from `ConfigurationController`, configuration UI/view models, ordinary API responses, config-diff/table-of-changes serialization, and journal payloads.
- Redact `hsm_pat_...` patterns in application/request logs and exception serialization.
- Do not include token values in audit events, telemetry, validation errors, tracing baggage, crash reports, UI analytics, shell examples with real values, or OpenAPI examples.
- Do not put tokens in process command-line arguments.
- Document `.env` as a supported simple profile only when the file is outside source control and protected by OS permissions.
- Recommend OS/deployment secret stores for stronger profiles.
- Add rate limiting/backoff for repeated invalid token attempts without creating an attacker-controlled unbounded cache. Resource scope remains independent of throttling.
- Record successful and rejected token use with safe identifiers, correlation ID, source information already permitted by HSM privacy policy, and result.
- Provide a server-side emergency operation to revoke all tokens for a user and, if necessary, all API tokens.
- Pepper configuration must fail safely. Do not silently generate an ephemeral pepper on every startup because that would invalidate persisted tokens unpredictably.

## Audit Events

Persist lifecycle events through `IJournalService`/`InitiatorInfo` using the token entity GUID, and persist per-request security events through the separate append-only security-event sink:

- Token created, with owner, creator, name, operation/resource grants, expiration, token entity GUID, and public token ID.
- Token restricted, with safe before/after metadata.
- Token rotated, linking old and new IDs.
- Token revoked, with initiator and reason.
- Token authentication succeeded, sampled/coalesced if necessary for volume but preserving security usefulness; write to the append-only security-event sink.
- Token authentication failed, rate-limited/coalesced without exposing secrets; write to the append-only security-event sink.
- Authorization denied, with token ID, subject ID, required permission, safe target identifier, and correlation ID; write to the append-only security-event sink.

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
ApiTokens.TokenRecordRetention
ApiTokens.InvalidAttemptRateLimit
```

Requirements:

- Safe default is disabled until a valid pepper is configured, unless installation tooling generates and persists one securely exactly once.
- Configuration validation occurs at startup with actionable errors.
- Pepper values must be treated as secrets by configuration diagnostics and must never be exposed or editable through the Configuration UI/API or written to journal/config-diff records.
- `ApiTokens.Enabled = false` is an emergency authentication/issuance kill switch: all API-token authentication plus create/rotate/restrict is denied immediately. Cookie-authenticated list/revoke and emergency revoke-all remain available for cleanup.
- `MaxTokensPerUser` counts only active, unexpired records. Revoked/expired records remain queryable for `TokenRecordRetention`, then a bounded cleanup job removes them; they never block new issuance.
- Limits and retention cleanup prevent unbounded token records and abuse.

## Work Breakdown

This architecture should be delivered in focused pull requests rather than one large change:

| Step | Scope | Notes |
|---|---|---|
| 1 | ADR, route convention, and permission inventory | Establish the `/api/v1` management-route convention and coexistence with current unversioned/Grafana routes; approve token/operation/boundary semantics and persistence compatibility. |
| 2 | Token domain and persistence | Versioned `HSMDatabase.AccessManager` entity/store, authoritative write-through memory state, grant canonicalization, orphan reaping, generation, verifier, lifecycle service, and tests. |
| 3 | Authentication scheme and policies | Handler, fail-closed `/api/v1` area convention, effective-rights intersection, resource authorization, lifecycle journal plus append-only security-event integration, and tests. |
| 4 | Token management UI/API | Cookie-only create/list/restrict/rotate/revoke, one-time secret handling, CSRF on mutations, and tests. |
| 5 | First read-only management endpoints | Prove IsAdmin read-only downgrade, ProductManager/ProductViewer behavior, and the unattended environment-token journey end to end. |

Each PR must update the actual behavior documentation and run focused server/security review. Do not expose broad management mutations until the authorization matrix and negative tests are established.

## Risks

- Principal replacement can restore unrestricted owner rights.
- Missing port isolation can expose management routes on port 44330.
- Flattening per-resource roles can allow cross-Product access.
- Rotation can escalate privileges unless cookie-only and non-expanding.
- Long-lived token disclosure grants access until revocation or expiration.
- Unattended self-rotation is out of scope in v1. Rotation requires a human cookie session and immediate atomic cutover, so operators must plan secret redeployment and a maintenance window; non-expiring tokens are not a substitute for that procedure.
- Pepper retirement requires coordinated token re-issue.

## Verification

### Cryptographic/token format tests

- Generated IDs and secrets have required byte lengths and valid Base64URL encoding.
- Conditional create never overwrites an existing LevelDB record; a forced ID collision retries with a new ID/secret pair.
- Large generation sample contains no duplicates.
- Plaintext token never appears in serialized persistence entities.
- Correct token verifies; any changed ID/secret/version fails. Tests pin `versionByte = 0x01` and the exact domain-separation byte sequence ending in `0x00`.
- Comparison uses the dedicated verifier boundary and malformed inputs fail safely.
- Missing/wrong pepper and unknown key ID fail closed.

### Lifecycle tests

- Create returns secret once; subsequent reads return metadata only.
- Revoke is immediate and idempotent.
- `ApiTokens.Enabled = false` immediately rejects existing-token authentication and new issuance/rotation while cookie-authenticated list/revoke remains available.
- Expired token fails.
- Rotation returns a new secret, preserves or reduces the source operation/resource grants and expiry, records `RotatedAtUtc`, and immediately invalidates the old token atomically.
- Restriction records `RestrictedAtUtc`/`RestrictedBy`; list/detail timestamps match persisted fields.
- Restriction removes operation/resource grants immediately.
- Grant expansion fails in place and through rotation; owner promotion does not create latent grants.
- Deleted owner invalidates token.
- Persistence survives server restart with the configured pepper.

### Authorization matrix tests

- An IsAdmin-owned token receives explicitly granted permissions only.
- An IsAdmin-owned read-only token cannot call any mutation or token-management endpoint.
- ProductManager cannot exercise IsAdmin-only operations or access another Product.
- ProductViewer cannot grant or exercise write permissions.
- No API token can change `IsAdmin` or user roles, create/manage collector access keys, read/mutate pepper material, or mutate server/global settings in v1.
- Owner role downgrade immediately reduces an existing token.
- Owner resource removal immediately reduces an existing token.
- Cross-Product and cross-Folder object access is denied.
- List responses do not leak unauthorized objects.
- Forged operation/boundary pairs in request payloads fail closed.
- A token with write on Product A and read on Product B never obtains write on Product B after owner promotion.
- Sensor access follows its current Product/Folder boundary, and moving it cannot retain access from the old boundary.
- `All` expansion does not include resources granted to the owner later. A Product later created/moved inside an explicitly granted Folder is covered; the same Product outside that Folder is not.

### HTTP/security tests

- Header authentication succeeds on the configured SitePort listener (default 44333); a cookie-only session is rejected by ordinary data-management `/api/v1/*` endpoints.
- Token in query/body/cookie is rejected; management routes are rejected on the configured SensorPort listener (default 44330).
- Missing, invalid, revoked, and expired tokens use the same observable status, response shape, and headers. Unknown IDs execute the fixed dummy-verifier path; tests assert constant-time secret comparison and bounded gross timing differences without relying on a flaky exact latency threshold.
- Failed authentication returns 401 (never cookie redirect), denied operation on an owner-visible resource returns 403, and owner-invisible or token-out-of-scope object access returns 404.
- Create/rotate response has `Cache-Control: no-store`.
- Logs, audit, tracing, exception output, and validation responses contain no full token or verifier.
- Oversized/malformed headers do not cause excessive allocation, exceptions, or database scans.
- Invalid-attempt limiting is bounded and does not block valid users globally.

### Compatibility tests

- Existing cookie login and authorization behavior remain unchanged.
- Existing collector access-key requests remain unchanged.
- MVC/Razor routes do not accept API tokens; UserProcessorMiddleware skips token principals; management controllers do not use BaseController.
- Versioned LevelDB entity upgrade/tolerant deserialization, orphan-token reaping, and backup/restore behavior are verified.

## Documentation Deliverables

- Keep this standalone initiative aligned with future control-plane work; OAuth/OIDC remains optional future work.
- Add canonical behavior documentation under `aicontext/features/server/api-tokens/feature.md` when implementation begins, following the existing server feature hierarchy.
- Document token creation, one-time display, restriction, rotation, revocation, and emergency response.
- Document environment-variable use and safer OS secret-store options.
- Add the operation/resource matrix under `aicontext/features/api/` and add the new terms to `aicontext/glossary.md`.
- Add an ADR under `docs/decisions/` and update `docs/decisions/INDEX.md`.

## Acceptance Criteria

- IsAdmin, ProductManager, and ProductViewer owners can create only explicit operation/resource grants allowed by current per-resource access.
- An IsAdmin user can create a read-only monitoring token, and automated tests prove that every covered mutation is denied with that token.
- The server never persists or logs the recoverable token value.
- A copied token authenticates through the HTTP `Authorization: Bearer` header after server restart.
- Revocation, expiration, owner deletion, role downgrade, resource removal, and token restriction affect subsequent requests immediately.
- Existing tokens cannot expand in place or through rotation; token management is cookie-only, while all other management `/api/v1` routes are `HsmApiToken`-only.
- Credential, user-role, access-key, pepper, and global-settings mutations are not grantable to API tokens in v1.
- Cookie and collector authentication remain compatible; management routes are unavailable on the configured SensorPort listener.
- Principal/scheme isolation, dual-listener isolation, privilege reduction, non-grantable high-risk operations, grant-pair non-recombination, hierarchy-move handling, orphan reaping, cross-resource denial, rotation, pepper secrecy/retirement, and secret-redaction tests pass.
- Canonical auth/API documentation and the architecture decision are updated from the implemented behavior.

## Implementation Questions Requiring Review

1. What operation matrix is granted by IsAdmin, ProductManager, and ProductViewer for each API capability?
2. Should No expiration require explicit confirmation or server policy?
3. Is same-user token-name uniqueness useful?
4. What retention policies apply separately to lifecycle journal records and the append-only per-request security-event sink?
5. How are pepper keys backed up and how is mass re-issue coordinated before retirement?
6. Should service accounts be a follow-up initiative?
7. Do any routes require an exception to the 403-visible / 404-out-of-scope policy?
