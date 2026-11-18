# Provision, not push: the server never installs agents on target machines

> Trial-series ADR (created by the skills trial, see `CLAUDE.md` → Documentation Rules; the canonical
> decision series lives in `docs/decisions/`). Numbering is independent of that series.

When an AI agent (an MCP client acting as orchestrator) installs an HSM agent on a target machine,
the server's role is **provisioning only**: create the product, issue the access key, serve the
installer bundle (with a one-time short-lived download link, so the orchestrator never needs the raw
bearer token in its shell). The actual installation always executes locally on the target machine by
the orchestrator's own means. The server deliberately gains no remote-execution capability (no
SSH/WinRM/PsExec push) and never stores credentials for target machines.

## Considered Options

- **Server pushes** (SSH/WinRM from HSM to targets) — rejected: turns the monitoring server into a
  deployment orchestrator with a stored credential vault for every monitored machine, a new attack
  surface disproportionate to the goal, and an operational burden (job queues, per-OS execution,
  failure reporting) that duplicates what the orchestrator already has.

## Consequences

- The `/api/v1` + MCP surface needed for agent installation is small: product create, access-key
  CRUD, bundle download. No deployment jobs, no agent-connection inventory on the server — with ONE
  bounded piece of state (#1797 review): the one-time download links hold the BUILT bundle bytes in
  memory for ~10 minutes (`AgentInstallerDownloadLinks`, a sweep-timer store). That state is lost on
  restart and NOT shared across instances — an unredeemed link dies with the process, and a
  multi-instance deployment would redeem links only on the issuing instance. Acceptable for a
  10-minute hand-over; do not build on statelessness behind a load balancer.
- "Is the installed agent alive?" is answered from data the server already keeps (the access key's
  `lastUseTime`/`lastIp` — the wire field names — and the product's sensors) — no new agent registry.
- If a push model is ever wanted, it must come as a separate initiative with its own security review;
  nothing in this decision's API shape blocks it, but nothing prepares it either.
