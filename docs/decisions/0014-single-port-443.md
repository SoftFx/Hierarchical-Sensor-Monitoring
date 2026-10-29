# ADR-0014: One port — 443 serves everything; the sensor/site port split is retired

**Status:** Accepted
**Date:** 2026-10-29
**Supersedes:** the SitePort-listener clause (3) of ADR-0003

---

## Context

Since the first public deployments HSM exposed two non-standard ports: 44333 (Web UI) and 44330 (Sensor API). Both Kestrel listeners share one routing table — the site listener already serves both surfaces and nothing is sensor-port-only (measured live; recorded in `aicontext/architecture/docker.md`). The split survived for one reason: clients already pointed at the old ports.

The cost is paid everywhere: firewall exceptions for non-standard ports on every deployment, blocked access from locked-down networks that allow only 80/443, and a port that must be configured and kept in sync in every client (DataCollector options, agent configs, browser bookmarks, Grafana datasources, CI). Since the bundled Caddy stack (#1427), the proxy already terminates 443 and forwards everything to the site listener — the management surface was already reachable there, so the port split no longer provided isolation in the standard deployment either.

## Decision

1. **The server listens on exactly one port** (`Kestrel.Port`, default 443). `Kestrel.SensorPort`/`Kestrel.SitePort` are gone; values found in an upgraded settings file are ignored with a startup warning and dropped by the first resave.
2. **443 is the primary client address** for everything: Web UI, Sensor API ingestion, management API, MCP, Swagger, agent downloads, VictoriaLogs read path. The bundled Caddy publishes 80 (redirect/ACME) and 443, and forwards to the single upstream.
3. **A bounded deprecation window** keeps 44330/44333 answering as aliases of the same address (Caddy site blocks and compose port mappings), so field clients are not stranded. The app logs a rate-limited warning when a request's Host carries a deprecated port — the operator's signal for stragglers. A follow-up release removes the aliases.
4. **The port dimension of the route guards is retired**: `HsmListenerBindings`, the SitePort-only arms of the management-area guard, the MCP gate and the Swagger gate are removed. The auth-level rules they shared (fail-closed `/api/v1` allow-list, bearer-only management policy, MCP endpoint policy, cookie-principal replacement rules) are unchanged and carry the isolation alone. This supersedes clause 3 of ADR-0003 in the part that keyed on the listener; every other clause of ADR-0003 stands.
5. **Client defaults move to 443** (DataCollector `DefaultPort`, agent config schema and templates, ping module, examples). No public signature changes.
6. **Agent bundles never bake a deprecated port**: the fallback port is the request host's port unless that port is a deprecated alias (then 443).

## Consequences

- No new public exposure: 443 already terminated at the site listener before this decision; the change removes ports rather than opening surface.
- The deployment topology "expose ingestion publicly while firewalling the site/management port" is no longer possible — inherent to one-port operation. The security boundary is auth: per-product access keys for ingestion, HsmApiToken bearer credentials (with per-request owner∩token grants) for management. Operators needing a hardened edge must front Caddy with their own firewall/path rules.
- Direct (no-Caddy) deployments publish `443:443` (plus the deprecated host-port aliases through the window); the container healthcheck probes the single internal listener.
- Agents and collectors pinned to `:44330` keep working through the window; they are repointed by reinstalling a server-generated bundle (which now carries 443) or editing config. The deprecation warning tells operators when the stragglers are gone — that is the signal to ship the alias removal.
- Unprivileged local dev cannot bind 443: `appsettings.Development.json` pins a high port for `dotnet run`; containers run as root and use the default.

## Alternatives Considered

- **Keep both ports forever**: rejected — every client and firewall carries the cost forever, and the isolation the split provided was already gone behind Caddy.
- **Honor legacy `SensorPort`/`SitePort` values as the listener port (site-port wins)**: rejected — the bundled stack (Caddy upstream, healthcheck, compose mappings) is built around the standard port; silently listening on a legacy value would break the same upgrades the honoring tries to protect.
- **Collapse only the public surface (Caddy publishes 443 only) but keep two internal listeners**: rejected — the second listener would be dead weight feeding a guard that can never fire, and the config surface would keep lying that two ports exist.

## References

- #1590 (task: serve everything on port 443, deprecation window)
- ADR-0003 (auth isolation this decision partially supersedes)
- ADR-0007 (bundled Caddy image)
- `aicontext/architecture/docker.md` (canonical deployment topology)
