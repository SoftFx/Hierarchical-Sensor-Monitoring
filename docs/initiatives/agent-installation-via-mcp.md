# Initiative: Agent installation via MCP (the provisioning path)

> Owner: server | Last reviewed: 2026-11-18 | Canonical: yes

## Problem

An AI agent (an MCP client acting as orchestrator on a target machine) cannot install an HSM agent
end-to-end today. Every step of the provisioning path is blocked on a missing `/api/v1` + MCP
surface, even though the server already has all the underlying machinery:

- creating a product is web-UI-only (`ProductController.CreateProduct`, cookie);
- issuing an access key is web-UI-only (`AccessKeysController`, cookie, `ProductManager` role);
- the installer bundle download is cookie + admin (`AgentController.Installer` / `LinuxInstaller`).

The same gap answers the broader "what is missing for server management" question partially; this
initiative deliberately takes only the installation path (the decisions below list what is deferred).

## Goals

- An orchestrator holding a management token can, via `/api/v1` REST and MCP twins: create a
  product, issue an access key, download the Windows/Linux installer bundle, and verify the installed
  agent connected — with no web-UI or admin involvement beyond the token itself.
- REST stays the canonical surface; every new endpoint gets an MCP twin (`aicontext/features/server/mcp/feature.md`).
- All decisions follow ADR-0005 (token = full mirror of its owner) and the area conventions of
  `aicontext/features/server/management-api/feature.md`.

## Non-Goals

- Remote execution: the server never installs anything on target machines — ADR `docs/adr/0001-provision-not-push.md`.
- Server administration over API: users CRUD, admin token management, server config, backup/restore,
  server restart — separate initiatives if ever pulled in.
- Domain parity debts: product/node edit/remove/move, chats CRUD, folders CRUD, MCP twins for the
  product-scope aggregate policy endpoints.
- A `/api/v1` server version/health endpoint.
- Master keys (server-wide, `ProductId == Guid.Empty`) — excluded from `/api/v1`.

## Current Behavior

> Historical — the state BEFORE the initiative; all Work Breakdown steps below have landed, and the
> delivered contract lives in `aicontext/features/server/agent-download/feature.md` (the bundle) and
> `aicontext/features/server/management-api/feature.md` / `mcp/feature.md` (the surfaces).

- Agent = standalone C++ Windows service (`src/agent`, `hsm-agent.exe`) or Linux probe; connects with
  a per-product access key (the GUID itself) in the `Key` header; no registration protocol.
- `AgentKeySelector.Select(product)` picks an existing send-data key for the bundle — the bundle
  endpoint does NOT generate keys. A usable key ALREADY exists right after product creation: every
  product is created with a `DefaultKey` (full permissions, never expires), and the selector prefers
  it; an explicitly created key is picked only when the DefaultKey is blocked, deleted or
  re-permissioned below usability.
- Product creation in the web UI: any authenticated user; the creator becomes the product author.
- Access-key management in the web UI: `ProductManager` role on the product; the key GUID is visible
  to entitled users (`AccessKeyViewModel.Id` is rendered).
- `AccessKeyModel` already tracks `LastUseTime`/`LastIP` — the "did the agent connect" signal exists.

## Proposed Direction

REST endpoints (canonical), all under the existing management-API conventions (uniform error
contract, OpenAPI annotations, journaling as TreeCrud, anti-enumeration 404/403 split):

```
POST   /api/v1/products                                        create (any read-write token; creator = author)
GET    /api/v1/products/{id}/accessKeys                        list (product sight; includes lastUseTime/lastIP/state)
GET    /api/v1/products/{id}/accessKeys/{keyId}                one key
POST   /api/v1/products/{id}/accessKeys                        create (ProductManager + RW token; 201 echoes the GUID)
PATCH  /api/v1/products/{id}/accessKeys/{keyId}                rename / permissions / block-unblock
DELETE /api/v1/products/{id}/accessKeys/{keyId}                delete
GET    /api/v1/products/{id}/agent-installer?os=windows|linux  stream the bundle (bearer; read-write OR read-only token)
POST   /api/v1/products/{id}/agent-installer/link?os=          issue the one-time link (bearer; READ-WRITE token — a POST)
GET    /api/agent/installer-download?token=<guid>              redeem the link ONCE, anonymously (outside /api/v1 by
                                                               design: the area guard 404s tokenless requests)
```

Decisions (resolved in the grilling session, 2026-11-18):

| Decision | Choice |
|---|---|
| Deployment model | Provision-not-push (ADR `docs/adr/0001`) |
| Scope | Installation path only |
| Access-key authz | Mirror web UI: reads by product sight, writes ProductManager + RW token; master keys excluded |
| Key secret exposure | Parity: the GUID is visible in list/get to sighted owners, like the web UI |
| Bundle authz | ProductManager at the product; a READ-ONLY token suffices for the REST `GET` stream ONLY — the link-issuing `POST` and every `/mcp` message are unsafe methods, which the policy backstop denies to read-only tokens outright (the orchestrator path needs a read-write token; #1797 review r2) |
| Bundle delivery | REST streams under bearer (read-write OR read-only); the link issuer is a `POST …/agent-installer/link` (read-write) answered with `{url, expiresAt, fileName, size, sha256}`; the link redeems ONCE, anonymously, at `GET /api/agent/installer-download?token=…` — the orchestrator never needs the raw token on the target machine |
| Product creation | Mirror web: any read-write token owner; the creating surface grants the owner the ProductManager role on the new product (the core/web flow grants none — an unattended creator would hold an id its token cannot see). Accepted consequence (pinned by a test, #1792 review r2): a resolvable product role opens the caller-wide gate (`CanSeeAnyBoundary`) for the creator — web already shows those resources to every logged-in user, and the PM role is what the key surface (#1787) is gated on |

MCP twins (six new tools; the count at planning time was 35, at delivery 41): `create_product`,
`list_access_keys`, `create_access_key`, `update_access_key`, `delete_access_key`,
`get_agent_installer` → `{url, expiresAt, fileName, size, sha256}`.

Non-contentious synthesis details:

- `create_access_key` takes explicit permissions; when omitted, the default is the agent profile
  `CanAddNodes | CanAddSensors | CanSendSensorData` (`CanReadSensorData` is not needed by an agent).
  NOTE the bundle does not necessarily embed THAT key: the selector prefers the product's
  full-permission `DefaultKey` (see Current Behavior) — the least-privilege story holds for keys an
  operator explicitly issues, while the out-of-the-box bundle key is the DefaultKey with ALL four
  permissions. Narrowing the bundle to an agent-profile key (a `keyId` parameter or a selector
  preference) is a recorded follow-up, not delivered behavior.
- "Agent connected" verification needs NO new endpoint: poll `list_access_keys` for
  `lastUseTime != null` on THE KEY THE AGENT ACTUALLY USES — the GUID embedded in the downloaded
  bundle's `config.json` (by default the `DefaultKey`, not a key you created alongside).
  `find_sensors` on the new product for first data works too.

End-to-end scenario after delivery:

1. `create_product {name}` → productId
2. OPTIONAL, only when the product has no usable key (the auto-created `DefaultKey` is blocked,
   deleted or re-permissioned below usability): `create_access_key` → key GUID
3. `get_agent_installer {os}` → one-time link
4. On the target machine (orchestrator's own shell): `curl <link>`, unpack, `install.cmd`/`install.sh`
   (the key embedded in `config.json` is the SELECTOR'S pick — by default the full-permission
   `DefaultKey`)
5. Poll `list_access_keys` until the `lastUseTime` of the key from step 4's `config.json` is set
   (by default: the `DefaultKey` entry).

## Work Breakdown

All steps landed (#1786 → PR #1792, #1787 → PR #1793, #1789 → PR #1794, #1788 → PR #1796; the smoke
below closes the initiative as #1790).

| Step | Scope | Notes |
|---|---|---|
| 1 ✅ | `POST /api/v1/products` + `create_product` MCP twin | PR #1792 — plus the creator's ProductManager-role grant (the round-1 review's fix) |
| 2 ✅ | Access-keys read surface (2 endpoints) + `list_access_keys` | PR #1793 |
| 3 ✅ | Access-keys write surface (3 endpoints) + 3 MCP twins | PR #1794 — plus the informational TreeCrud audit trail |
| 4 ✅ | `GET …/agent-installer` (bearer stream) + one-time links + `get_agent_installer` | PR #1796 — the redemption endpoint lives under `/api/agent/` (the `/api/v1` guard 404s tokenless requests) |
| 5 ✅ | Docs + tests everywhere (each PR); the E2E smoke transcript below | #1790 |

## End-to-end verification (#1790)

The full scenario against a locally run server of the merged branch (server 3.43.0, fresh LevelDB,
a placeholder-staged `hsm-agent.exe`; the only web-UI step is the operator's one-time token issuance —
exactly the initiative's credential model):

0. **Operator credential** (the only cookie step): log in `default`/`default`, mint a read-write
   management token via `POST /Profile/CreateToken` → `hsm_pat_v1_…` (shown once, never stored).
1. **MCP initialize** — `POST /mcp` with the bearer (stateless streamable HTTP, protocol 2025-06-18):
   `{"serverInfo":{"name":"HSMServer","version":"3.43.0"}}`.
2. **`create_product`** → `{"id":"83c186ab-…","name":"Smoke Agent Product"}` (first run) /
   `{"id":"57894232-…","name":"Smoke DefaultKey Product"}` (second run, below).
3. **`create_access_key {displayName:"smoke-agent-key"}`** (first run — demonstrating the API, NOT a
   required step; the product already owns a usable `DefaultKey`): agent-profile permissions
   `["canSendSensorData","canAddNodes","canAddSensors"]` and `expirationTime: 9999-12-31`
   (the born-expired regression #1794's review caught — green live).
4. **`get_agent_installer {os:"windows"}`** →
   `{url:"…/api/agent/installer-download?token=72a5075d-…", expiresAt:+10min, size:3769, sha256:"fdd8719d…"}`.
   Curl the URL on "the target machine" (no Authorization header): `200`, 3769 bytes,
   `sha256 == fdd8719d…` (the advertised hash pins the served bytes); a second fetch → `404`
   (single-use, live). The zip carries `hsm-agent.exe`, `config.json`, install/uninstall scripts,
   README; `config.json` embeds the SELECTOR'S pick with the address split correctly
   (`https://<host>` + separate port — the #1796 review's resolver fix) — in both runs that was the
   product's **`DefaultKey`** (`6f642ef7-…` / `42ceb5a0-…`), NOT the key created in step 3.
5. **"The agent connects" — poll THE EMBEDDED key, not one you created** (the #1797 review's point,
   re-verified live on the second run): `list_access_keys` before → the `DefaultKey` with
   `lastUseTime: null` and permissions `["canSendSensorData","canAddNodes","canAddSensors",
   "canReadSensorData"]` (FULL permissions — the least-privilege caveat below); the "agent" presents
   the embedded key — `GET /api/sensors/testConnection` with `Key: 42ceb5a0-…` → `200`; two seconds
   later `list_access_keys` shows THAT key's `lastUseTime: 2026-11-18T23:41:…Z`,
   `lastIp: 127.0.0.1` — the agent-connected signal with NO new endpoint. (The first run had
   presented the step-3 key instead — that lights ITS OWN `lastUseTime` too, but following the
   installed agent's traffic means watching the EMBEDDED key.)

Step 4's literal service installation (`install-hsmagent-service.cmd` on a real host) is the only
part simulated (the placeholder exe); everything above it — admission, credential issuance, link
hand-over, redemption integrity, first use — ran on the real wire.

## Risks

- The one-time link URL must be host-absolute for a curl on the target machine — host resolution must
  come from the request host (the `AgentConnectionResolver` fallback pattern), not from
  `ExternalConnectionUrl` (that one is the agent's data channel, not the management API).
- Key GUID visibility in list/get is deliberate (web parity) — document it loudly in the DTO remarks;
  a read-only token of a manager reads credentials, exactly as that manager's cookie session can.
- Product creation via API removes the web UI's implicit rate-limiting-by-UX; tree growth is bounded
  only by policy (same as web today — no new guard invented here).
- **Least-privilege gap, deferred** (#1797 review): the out-of-the-box bundle embeds the product's
  full-permission `DefaultKey` (the selector's preference), so the credential that reaches the
  target machine carries `CanReadSensorData` even though the initiative's agent profile does not
  need it. Follow-up options: a `keyId` parameter on the installer endpoints/tools, or a selector
  preference for agent-profile keys. Until then an operator wanting least privilege creates a
  narrow key AND blocks/deletes the `DefaultKey` before fetching the bundle.

## Verification

- Unit/controller tests: the authorization matrix above; uniform error contract on every new path.
- MCP parity tests per `aicontext/features/server/mcp/tests.md`.
- Smoke: the end-to-end scenario (steps 1–5) against a dev server with a PM token.
