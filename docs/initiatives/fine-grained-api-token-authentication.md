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
- Existing Grafana datasource routes have no ASP.NET authentication scheme but validate an access key from the request. The existing Swagger UI/OpenAPI (`api/swagger`) is anonymous and reachable on both listeners. This initiative must not silently change either existing surface.
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
- An existing token's explicit operation/boundary grant pairs can never expand, including through rotation. Effective resource membership may grow only through the deliberate dynamic-membership semantics of an explicitly granted Folder boundary.
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

The v1 textual format is fixed: `hsm_pat_v1_` followed by the canonical 22-character unpadded Base64URL token ID, `.`, and the canonical 43-character unpadded Base64URL secret. Parsers reject padding, non-Base64URL characters, alternate encodings, wrong lengths, and any decoded value that does not re-encode to the exact presented text.

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

- Entity ID and a non-sensitive token display hint; never the full authentication `TokenId`.
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

- Token ID: exactly 128 random bits, encoded as exactly 22 canonical unpadded Base64URL characters. It is a public lookup key, not a secret.
- Token secret: 256 random bits generated by `RandomNumberGenerator.GetBytes(32)`, encoded as exactly 43 canonical unpadded Base64URL characters.
- Token prefix/version: `hsm_pat_v1_` maps only to `versionByte = 0x01`; the parsed version must equal the persisted record `VersionByte` before authentication can succeed.
- Randomness must come only from `System.Security.Cryptography.RandomNumberGenerator`.
- Do not use `Random`, timestamps, usernames, counters, hashes of user data, or a GUID as the only secret material.

### Stored verifier

HSM stores an irreversible verifier, never plaintext or reversibly encrypted token material:

```text
verifier = SHA-256(
    ASCII("HSM-API-TOKEN") || 0x00 || versionByte[1] || tokenIdBytes[16] || tokenSecretBytes[32]
)
```

Requirements:

- No deployment-wide pepper or additional token-verification secret exists. The database contains the verifier and all metadata required after backup/restore.
- A read-only database leak does not reveal the uniformly random 256-bit token secret or make exhaustive recovery feasible. Database write/integrity compromise can replace verifier records and is outside the protection offered by token hashing.
- Verification uses `CryptographicOperations.FixedTimeEquals`.
- Temporary byte buffers containing token secrets should be cleared with `CryptographicOperations.ZeroMemory` when practical.
- Token parsing must reject malformed/oversized inputs before database access or expensive work.

A single SHA-256 verifier is appropriate because the server generates a uniformly random 256-bit secret. Password hashing algorithms such as Argon2id/PBKDF2/bcrypt are required for low-entropy human passwords, not for server-generated 256-bit credentials. A separate pepper stored alongside the database would not add meaningful protection, so v1 deliberately has no pepper. Users must not be allowed to choose token secrets.

### Why opaque tokens

Opaque tokens are selected instead of JWTs because HSM requires:

- Immediate revocation.
- Immediate reaction to owner role/resource changes.
- Server-controlled token metadata and last-used tracking.
- No authorization claims frozen into a long-lived client-visible credential.

Every authenticated request resolves token and current-owner state from an authoritative store. The token store may reuse existing lifecycle and dependency-injection conventions, but it must not reuse generic `ConcurrentStorage` creation/write ordering because that publishes memory before persistence. Create/restrict/rotate/revoke, owner deletion, role changes, and resource moves synchronously update or invalidate authoritative state. Secondary stale caches without complete invalidation are forbidden; any bounded stale interval requires a separate security decision.

## Persistence Model

Add a versioned `ApiTokenEntity` collection/store under `HSMDatabase.AccessManager`, accessed only through a dedicated token manager/lifecycle service. A dedicated token store owns persist-first publication and the in-memory authentication index; generic `ConcurrentStorage.TryAdd` is forbidden for token creation. LevelDB compatibility uses entity versioning and tolerant fail-closed deserialization rather than a relational migration framework.

Required fields:

```text
EntityId              stable GUID for in-memory identity, lifecycle management routes, and entity-keyed journal records
TokenId               public random 128-bit authentication lookup key; unique index in the authoritative token store
VersionByte           exactly 1 byte; 0x01 for hsm_pat_v1 token/verifier format
Verifier              32-byte SHA-256 result
OwnerUserId           current owning user/subject
GlobalRevocationGenerationAtIssue  monotonic generation captured at creation
OwnerRevocationGenerationAtIssue   monotonic owner generation captured at creation
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
RotatedFromEntityId    nullable stable entity GUID
RevokedAtUtc           nullable
RevokedBy              nullable
RevocationReason       nullable, sanitized
```

Persistence rules:

- Never serialize plaintext token values.
- Never reuse a token ID or secret. Implement a dedicated `TryInsertApiToken` persistence primitive whose existence check and LevelDB write are serialized atomically inside the database worker/store boundary. Lifecycle code must not compose an unlocked read plus `Put` and must not publish through generic `ConcurrentStorage.TryAdd`. Persistence succeeds before the record enters the authoritative in-memory authentication index; failure leaves neither durable nor live state. A collision discards the whole candidate and retries with a new ID/secret pair. Blind overwrite is forbidden; the 128-bit collision path is a defensive invariant, not an expected event.
- Creation of metadata and verifier must be atomic from the caller's perspective.
- A failure after persistence but before the one-time response must revoke/delete the unusable record safely; do not attempt to expose it later.
- Revocation is idempotent.
- Persist monotonic global and per-owner revocation generations in the token store. Every authentication compares the token's issuance generations with current authoritative values; missing, unreadable, or regressed generation state fails authentication closed. New tokens capture the current generations.
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
system-health:read
```

This list is illustrative until the API capability inventory is approved. Requirements:

- Each API operation maps to one or more canonical permissions.
- Each permission declares the minimum current owner role/condition that can exercise it.
- Token permission selection is an allow-list; absence means denied.
- `*`, `admin`, controller-name permissions, and implicit write-through-read permissions are forbidden in the initial implementation.
- Read and write are separate.
- Destructive, credential, user-role, backup/restore, access-key, and secret-bearing configuration operations are non-grantable and cookie-only in v1. No `users:*`, `access-keys:*`, `credentials:*`, or `server-settings:*` permission exists in the v1 token catalog. A future metadata-only capability requires a dedicated threat review and response DTO that cannot project `User.Password`, an access-key `Id`, master keys, tokens, or any other credential material.
- Grantable management APIs use dedicated credential-free DTOs and never serialize existing `User`, `UserViewModel`, `AccessKeyModel`, or `AccessKeyViewModel` types.
- Permission checks do not replace object/resource authorization.

### Mandatory privilege-reduction examples

| Owner | Token grant | Expected result |
|---|---|---|
| IsAdmin | Read grants for `products`, `sensors`, and `history` on selected Product/Folder boundaries | Read-only monitoring token; every mutation returns 403. |
| IsAdmin | `alerts:read` bound to one Product | May read alerts only inside that Product; cannot read unrelated sensor history or change alerts. |
| ProductManager on Product A | Read/write alert grants bound to Product A | Allowed only while the owner retains ProductManager authorization for Product A. |
| ProductViewer on Product A | Forged `alerts:write` grant for Product A | Creation returns `403 Forbidden`; runtime mutation is denied in all cases. |
| Any owner later downgraded | Previously broader token | Effective access is reduced immediately without changing the token record. |

## Resource Scope

In v1, resource-scoped grants bind operations to stable Product or Folder IDs. A Sensor is an authorization target, not an independently selectable scope ID: it inherits the current Product/Folder boundary resolved from the live hierarchy. Global operations use a distinct explicit global boundary.

Requirements:

- `All available boundaries` is a UI convenience only: creation expands it to concrete Product/Folder boundary IDs, and no wildcard boundary is persisted. Each selected operation is bound to each concrete boundary in the persisted grant.
- An explicit Folder-boundary grant stores the Folder kind and stable ID, not a snapshot of descendants, and intentionally covers current and future descendants. This is the sole accepted dynamic-membership case. The UI warns that effective reach can grow and requires explicit confirmation. Product boundaries are preferred for sensitive workloads; Folder creation/move operations are audited as access-affecting changes. Deletion fails closed, and recreating a similarly named Folder with another ID never restores the old grant.
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
3. Parse prefix, version, ID, and secret with strict limits and the canonical Base64URL rules above.
4. Load token metadata by public ID and retain an explicit record-found marker. Select the stored verifier when found or a fixed dummy stored verifier when missing, without exposing which path was selected.
5. Always compute the candidate SHA-256 verifier from the presented parsed version, ID, and secret, then compare it with the selected stored-or-dummy verifier using `FixedTimeEquals`. A missing-record marker forces the same generic failure after comparison; only a found record with matching `VersionByte` and verifier proceeds to revocation, expiration, and owner/resource checks.
6. After successful secret verification, check revocation, expiration, and owner existence.
7. Load current IsAdmin and per-Product/per-Folder roles.
8. Build a minimal ClaimsPrincipal with owner ID, token ID, and authentication-scheme identity.
9. Return one generic authentication failure without revealing which validation failed.

The handler never exposes the unrestricted stored User as the token authorization result.
The handler should orchestrate authentication only. Token lifecycle, verification, permission intersection, resource authorization, and auditing belong in dedicated services/domain components, not controllers.

### Scheme and port isolation

- Cookie authentication remains the default authenticate/challenge scheme. The ASP.NET `DefaultPolicy` used by bare `[Authorize]` is explicitly pinned to the cookie scheme so legacy MVC/Razor authorization can never succeed with an API-token identity.
- `HsmApiToken` is not a default, forwarded, or policy scheme and is invoked only by an explicit management policy. Every data-management `/api/v1/*` endpoint except `/api/v1/api-tokens` selects a policy restricted to `HsmApiToken` and rejects a cookie-only principal. Plain `[Authorize]` with the default cookie scheme is forbidden on these controllers. Legacy MVC/Razor and `BaseController` routes never invoke the token handler or token-store lookup.
- `/api/v1/api-tokens` is the sole v1 cookie-only route family and never falls back to `HsmApiToken`.
- Collector access-key validation remains unchanged.
- The management API is hosted only on the configured Kestrel SitePort listener (default 44333).
- Apply a fail-closed `/api/v1` endpoint convention/metadata policy to the whole management area: endpoints are allow-listed only on the SitePort listener and rejected everywhere else, including SensorPort, before controller execution. A newly added `/api/v1` route without the required metadata/policy is unavailable by default.
- Populate one immutable `HsmListenerBindings` registry while configuring Kestrel and inject that same registry into the area guard and authentication middleware. Do not independently capture mutable config values; configuration changes take effect only when listeners and the registry are rebuilt on restart.
- A successfully authenticated token principal contains exactly one identity with authentication type `HsmApiToken`. `UserProcessorMiddleware.cs` short-circuits only for that explicit identity and never replaces or nulls it. Mixed/multiple identities fail closed, and the token handler never sets `Identity.Name` to a stored user login.
- Management controllers derive from `ControllerBase`, not `BaseController`.
- A fail-closed isolation guard rejects an `hsm_pat_` bearer credential on every non-`/api/v1` route before MVC/Razor controller execution. It must return a non-redirecting generic `401` and must never render a page, execute an `[Authorize]`-only action, or allow a `BaseController` cast to produce `500`.
- Every token-management endpoint requires an interactive cookie session. State-changing methods require anti-forgery protection; GET list/detail routes must be side-effect-free.
- API tokens cannot create, list, inspect, restrict, rotate, or revoke tokens.
- Tests exercise both listeners, fail-closed behavior for a new `/api/v1` route missing metadata, the principal-replacement path, cookie rejection on data-management routes, API-token rejection on lifecycle routes, and a valid HSM bearer sent to both `[Authorize]`-only and `BaseController` legacy routes returning generic `401` rather than success, redirect, or `500`.
- The existing collector/Grafana OpenAPI document, `api/swagger` UI, and existing Key/ClientName header behavior remain unchanged on both listeners. Management v1 uses a separate OpenAPI document and UI entry available only on SitePort. It is absent on SensorPort, excludes legacy `DataRequestHeaderSwaggerFilter` Key/ClientName requirements, declares `HsmApiToken` bearer security without real token examples, and distinguishes cookie-only lifecycle routes.

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
- After successful authentication, detail/mutation returns `404` when the target is absent, invisible to the current owner, or no token grant covers its current boundary. It returns `403` only when the boundary is covered and the owner can see the target, but the required operation is absent or the owner currently cannot perform it. List endpoints filter on both owner visibility and token boundary/operation.
- Malformed grant syntax, unknown operation/boundary kind, or structurally impossible grant shape returns `400 Bad Request`. A well-formed grant that the current owner is not authorized to issue, or an attempted expansion of an existing token, returns `403 Forbidden`.
- Revoking an already revoked token: idempotent success.
- Secret is returned only by successful create/rotate responses and is marked `Cache-Control: no-store`.
- Token-management responses never echo an incoming bearer token.
- Emergency revoke succeeds with `204 No Content`; missing/invalid confirmation or reason returns `400`; missing cookie authentication returns non-redirecting `401`; an authenticated non-IsAdmin caller returns `403`; an unknown target owner returns `404`; and any partial persistence/cache failure returns generic retryable `503` without reporting false success.

## Proposed Token Management Endpoints

Exact routes must follow the versioned management API convention:

```text
POST   /api/v1/api-tokens                 create and reveal once
GET    /api/v1/api-tokens                 list own token metadata
GET    /api/v1/api-tokens/{entityId}                    get own token metadata
PATCH  /api/v1/api-tokens/{entityId}/restrict           remove permissions/resources or shorten expiry
POST   /api/v1/api-tokens/{entityId}/rotate             issue replacement and reveal once
DELETE /api/v1/api-tokens/{entityId}                    revoke idempotently
POST   /api/v1/api-tokens/emergency/users/{ownerUserId}/revoke  revoke all tokens for one user
POST   /api/v1/api-tokens/emergency/revoke-all          revoke every API token
```

All endpoints in this section are cookie-session-only initially. `{entityId}` is the stable token entity GUID, never the public authentication `TokenId`. Personal list/detail responses return `EntityId` plus a non-sensitive display hint, not the full public `TokenId`, secret, or verifier.

The two emergency endpoints require an IsAdmin cookie session, anti-forgery protection, a required sanitized reason, and an explicit confirmation value naming the target user or whole deployment. They are idempotent and remain callable when `ApiTokens.Enabled = false`. Revoke-user atomically increments one durable owner revocation generation; revoke-all atomically increments the durable global generation. The storage boundary persists the new generation before publishing it to authentication, and authentication checks it on every request. This single-record generation change invalidates the entire target set before `204` success; per-token revoked metadata is reconciled afterward in bounded, retryable maintenance. If the generation write fails, no partial per-token cleanup starts, `503` is returned with a correlation ID, and the previous generation remains authoritative. If durable generation succeeds but in-memory publication cannot complete, token authentication enters an unavailable fail-closed state and rejects all API tokens until the authoritative generations reload; it never continues with the old generation. Missing/corrupt generation state also rejects authentication. The lifecycle audit records initiator, scope, reason, affected count, generation, completion/failure, and correlation ID. Later non-emergency administrative management for another user requires a separate threat review.

## Secret Handling and Operational Security

- HTTPS is mandatory; validate server certificates.
- Never accept tokens in URLs, query strings, cookies, or request bodies for ordinary authentication.
- Redact `hsm_pat_...` patterns in application/request logs and exception serialization.
- Do not include token values in audit events, telemetry, validation errors, tracing baggage, crash reports, UI analytics, shell examples with real values, or OpenAPI examples.
- Do not put tokens in process command-line arguments.
- Document `.env` as a supported simple profile only when the file is outside source control and protected by OS permissions.
- Recommend OS/deployment secret stores for stronger profiles.
- Add rate limiting/backoff for repeated invalid token attempts without creating an attacker-controlled unbounded cache. Resource scope remains independent of throttling.
- Record successful and rejected token use with safe identifiers, correlation ID, source information already permitted by HSM privacy policy, and result.
- Keep the IsAdmin-only emergency revoke-user and revoke-all operations available even when token authentication is disabled; never expose them as token grants.

## Audit Events

Persist lifecycle events through `IJournalService`/`InitiatorInfo` using the token entity GUID, and persist per-request security events through the separate append-only security-event sink:

- Token created, with owner, creator, name, operation/resource grants, expiration, token entity GUID, and public token ID.
- Token restricted, with safe before/after metadata.
- Token rotated, linking old/new `EntityId` and `TokenId` values; every replacement receives fresh values for both identifiers.
- Token revoked, with initiator and reason.
- Emergency user/deployment revocation, with IsAdmin initiator, scope, sanitized reason, affected token count, old/new revocation generation, completion/failure, and correlation ID.
- Token authentication succeeded, sampled/coalesced if necessary for volume but preserving security usefulness; write to the append-only security-event sink.
- Token authentication failed, rate-limited/coalesced without exposing secrets; write to the append-only security-event sink.
- Authorization denied, with token ID, subject ID, required permission, safe target identifier, and correlation ID; write to the append-only security-event sink.

Audit storage must never contain secret or verifier values.

## Configuration

Introduce an explicit configuration section, names illustrative:

```text
ApiTokens.Enabled
ApiTokens.DefaultLifetime
ApiTokens.AllowNoExpiration
ApiTokens.MaxTokensPerUser
ApiTokens.TokenRecordRetention
ApiTokens.InvalidAttemptRateLimit
```

Requirements:

- `ApiTokens.Enabled` defaults to false for upgrades and must be enabled explicitly. Token verification requires no deployment secret beyond the persisted database.
- Configuration validation occurs at startup with actionable errors.
- `ApiTokens.Enabled = false` is an emergency authentication/issuance kill switch: all API-token authentication plus create/rotate/restrict is denied immediately. Cookie-authenticated list/revoke and IsAdmin emergency revoke-user/revoke-all remain available for cleanup.
- `MaxTokensPerUser` counts only active, unexpired records. Revoked/expired records remain queryable for `TokenRecordRetention`, then a bounded cleanup job removes them; they never block new issuance.
- Limits and retention cleanup prevent unbounded token records and abuse.

## Work Breakdown

This architecture should be delivered in focused pull requests rather than one large change:

| Step | Scope | Notes |
|---|---|---|
| 1 | ADR, route convention, and permission inventory | Establish the `/api/v1` management-route convention and coexistence with current unversioned/Grafana routes; approve token/operation/boundary semantics and persistence compatibility. |
| 2 | Token domain and persistence | Versioned `HSMDatabase.AccessManager` entity/store, dedicated persist-first `TryInsertApiToken`, post-persistence authentication index, grant canonicalization, orphan reaping, generation, verifier, lifecycle service, and tests. |
| 3 | Authentication scheme and policies | Handler, fail-closed `/api/v1` area convention, effective-rights intersection, resource authorization, lifecycle journal plus append-only security-event integration, and tests. |
| 4 | Token management UI/API | Cookie-only create/list/restrict/rotate/revoke plus IsAdmin emergency revoke-user/revoke-all, one-time secret handling, confirmations, CSRF on mutations, audit, and tests. |
| 5 | First read-only management endpoints | Prove IsAdmin read-only downgrade, ProductManager/ProductViewer behavior, and the unattended environment-token journey end to end. |

Each PR must update the actual behavior documentation and run focused server/security review. Do not expose broad management mutations until the authorization matrix and negative tests are established.

## Risks

- Principal replacement can restore unrestricted owner rights.
- Missing port isolation can expose management routes on port 44330.
- Flattening per-resource roles can allow cross-Product access.
- Rotation can escalate privileges unless cookie-only and non-expanding.
- Long-lived token disclosure grants access until revocation or expiration.
- Unattended self-rotation is out of scope in v1. Rotation requires a human cookie session and immediate atomic cutover, so operators must plan secret redeployment and a maintenance window; non-expiring tokens are not a substitute for that procedure.
- Database confidentiality protects verifier metadata, while database integrity/write compromise can replace token records and must be handled by normal HSM backup, access-control, and incident-response controls.
- A Folder-bound token automatically covers future descendants and resources moved into that Folder. Prefer Product boundaries for sensitive data and treat Folder creation/moves as audited access-affecting operations.

## Verification

### Cryptographic/token format tests

- Generated IDs and secrets have the required byte lengths and canonical unpadded Base64URL encodings: exactly 22 and 43 characters respectively. Padding, invalid alphabet characters, wrong lengths, and non-canonical aliases are rejected.
- Dedicated `TryInsertApiToken` serializes existence check plus LevelDB write inside the storage boundary and publishes to the authentication index only after persistence. Concurrent forced collisions never overwrite an existing record and retry with a complete new ID/secret pair; injected write failure leaves neither durable nor live state.
- Large generation sample contains no duplicates.
- Plaintext token never appears in serialized persistence entities.
- Correct token verifies; any changed ID/secret/version fails. Tests pin the `hsm_pat_v1_` to `versionByte = 0x01` mapping, require equality with the record `VersionByte`, and pin the domain-separation prefix `ASCII("HSM-API-TOKEN") || 0x00` plus the complete fixed-length input ordering.
- Comparison uses the dedicated verifier boundary and malformed inputs fail safely.
- Unknown token IDs always hash the presented candidate, compare against the fixed dummy stored verifier, and fail closed after comparison; no unknown ID can authenticate.

### Lifecycle tests

- Create returns secret once; subsequent list/detail reads return only `EntityId`, a non-sensitive display hint, and metadata; they never return the full authentication `TokenId`, secret, or verifier.
- Revoke is immediate and idempotent.
- `ApiTokens.Enabled = false` immediately rejects existing-token authentication and new issuance/rotation while cookie-authenticated list/revoke and IsAdmin emergency revoke-user/revoke-all remain available.
- Expired token fails.
- Rotation returns a new secret, preserves or reduces the source operation/resource grants and expiry, records `RotatedAtUtc`, and immediately invalidates the old token atomically.
- Restriction records `RestrictedAtUtc`/`RestrictedBy`; list/detail timestamps match persisted fields.
- Restriction removes operation/resource grants immediately.
- Grant expansion fails in place and through rotation; owner promotion does not create latent grants.
- Deleted owner invalidates token.
- Persistence survives server restart and a consistent backup/restore of token records together with owner/access state, without any separate token-verification secret.
- Deterministic-clock tests prove that N active tokens block N+1 at `MaxTokensPerUser`; revoked, expired, and orphaned records do not count; rotation at the cap atomically replaces the source slot without consuming another slot.
- Deterministic-clock retention tests pin the exact cutoff semantics. Cleanup uses bounded batches, eventually drains eligible records, preserves newer revoked/expired records, is restart/failure safe, and leaves lifecycle/security-audit retention independent.
- IsAdmin emergency revoke-user/revoke-all requires cookie authentication, CSRF, confirmation, and reason. Tests prove the persist-first per-owner/global generation increment invalidates all pre-generation tokens before `204`, survives restart, permits newly issued tokens at the new generation, and reconciles per-token metadata in bounded retryable batches. Injected generation-write failure performs no partial cleanup and returns `503` with correlation. Injected post-persistence publication failure makes all API-token authentication unavailable until generation reload; missing/corrupt/regressed generation state rejects authentication. Tests also assert `400/401/403/404` mappings and disabled-mode availability.

### Authorization matrix tests

- An IsAdmin-owned token receives explicitly granted permissions only.
- An IsAdmin-owned read-only token cannot call any mutation or token-management endpoint.
- ProductManager cannot exercise IsAdmin-only operations or access another Product.
- ProductViewer cannot grant or exercise write permissions.
- No API token can read or change user credentials/roles, read or manage collector access-key IDs, or read/mutate secret-bearing server/global settings in v1. Grantable read DTOs contain no `User.Password`, access-key `Id`, master key, token, verifier, or other credential material.
- Owner role downgrade immediately reduces an existing token.
- Owner resource removal immediately reduces an existing token.
- Cross-Product and cross-Folder object access is denied.
- List responses do not leak unauthorized objects.
- Forged operation/boundary pairs in request payloads fail closed.
- A token with write on Product A and read on Product B never obtains write on Product B after owner promotion.
- Sensor access follows its current Product/Folder boundary. Tests cover new descendants, move into/out of a granted Folder, delete/recreate with the same name but a different ID, and prove that access from an old boundary never survives.
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

- Existing cookie login and authorization behavior remain unchanged; a valid HSM bearer on legacy `[Authorize]` and `BaseController` routes performs no token lookup and receives generic non-redirecting `401`, never success or `500`. Mixed/multiple identities fail closed without principal replacement.
- Existing collector access-key requests and the existing default collector Swagger/OpenAPI UI on both listeners remain unchanged; the management OpenAPI group is absent on SensorPort.
- MVC/Razor routes do not accept API tokens; UserProcessorMiddleware skips token principals; management controllers do not use BaseController.
- Versioned LevelDB entity upgrade/tolerant deserialization, revocation-generation recovery, orphan-token reaping, and backup/restore behavior are verified.

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
- Explicit operation/boundary pairs cannot expand in place or through rotation; deliberate future-descendant membership under a confirmed Folder grant is the only dynamic-scope exception. Token management is cookie-only, while all other management `/api/v1` routes are `HsmApiToken`-only.
- User, credential, access-key, and secret-bearing global-settings operations are not grantable to API tokens in v1; no grantable response exposes stored credential material.
- Cookie and collector authentication remain compatible; management routes are unavailable on the configured SensorPort listener.
- Principal/scheme isolation, legacy-route bearer rejection, dual-listener/OpenAPI isolation, privilege reduction, credential-safe reads, emergency revocation, quota/retention, grant-pair non-recombination, hierarchy-move handling, orphan reaping, cross-resource denial, rotation, verifier persistence, and secret-redaction tests pass.
- Canonical auth/API documentation and the architecture decision are updated from the implemented behavior.

## Implementation Questions Requiring Review

1. What operation matrix is granted by IsAdmin, ProductManager, and ProductViewer for each API capability?
2. Should No expiration require explicit confirmation or server policy?
3. Is same-user token-name uniqueness useful?
4. What retention policies apply separately to lifecycle journal records and the append-only per-request security-event sink?
5. Should service accounts be a follow-up initiative?
6. Do any routes require an exception to the 403-visible / 404-out-of-scope policy?
