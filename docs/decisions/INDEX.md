# Architecture Decision Records

> Owner: shared | Last reviewed: 2026-09-16 | Canonical: yes

Use ADRs for durable decisions that future maintainers will ask about.

## Rules

- File name: `NNN-short-kebab-title.md`.
- Status: `Accepted`, `Superseded by ADR-NNN`, or `Deprecated`.
- Update this index whenever an ADR is added or superseded.
- Keep ADRs about decisions, not implementation changelogs.

## Records

| ADR | Status | Title | Date |
|---|---|---|---|
| 0001 | Accepted | [Remove user-facing node-level alert creation on non-leaf nodes](0001-node-level-alert-removal.md) | 2026-06-22 |
| 0002 | Superseded by ADR-0005 | [The API token operation catalog is append-only; renames and removals require a migration](0002-api-token-operation-catalog-append-only.md) | 2026-09-01 |
| 0003 | Accepted | [HsmApiToken is an isolated, non-default scheme behind a fail-closed /api/v1 area](0003-hsm-api-token-scheme-isolation.md) | 2026-09-01 |
| 0004 | Superseded by ADR-0005 | [No-expiration API tokens are the issuance default](0004-no-expiration-tokens-default.md) | 2026-09-09 |
| 0005 | Accepted | [An API token is an eternal owner mirror with a read-only flag](0005-api-token-owner-mirror.md) | 2026-09-10 |
| 0006 | Accepted | [Token usage sensors are keyed by owner login + token EntityId](0006-token-usage-sensors-keyed-by-owner-and-entityid.md) | 2026-09-16 |
| 0007 | Accepted | [Distribute Caddy with DNS challenge modules as a pinned image](0007-bundled-dns-caddy-image.md) | 2026-09-24 |
| 0008 | Accepted | [VictoriaLogs for HSM server log storage in the docker-compose deployment](0008-victorialogs-log-storage.md) | 2026-09-25 |
| _template | Template | [ADR template](_TEMPLATE.md) | — |
