# ADR-0004: No-expiration API tokens are the issuance default

**Status:** Superseded by ADR-0005 (2026-09-10, #1384 — token expiry was removed entirely; all tokens are eternal owner mirrors)
**Date:** 2026-09-09
**Supersedes:** —

---

## Context

Since the token-management UI landed (step 4 PR A), `ApiTokens.AllowNoExpiration` defaulted to `false`: an unlimited credential existed only where an operator explicitly accepted the trade, and `DefaultLifetime` (90d) was the create form's preselect either way.

The audience says otherwise: the typical API token is an agent/integration credential that outlives any preset, and forcing a renewal date on it was the worse default — tokens dying under a running Grafana dashboard or a resident agent is the failure mode users actually hit. #1373 asked for "No expiration" to be offered and preselected.

## Decision

`ApiTokens.AllowNoExpiration` defaults to `true`, and the create form preselects "No expiration" (its warning visible from the start). `DefaultLifetime` remains the preselect wherever the operator switched the gate off. The knob is now the operator's **opt-out**.

One knob covers both the offer and the preselect: "offer the option but preselect a finite lifetime" is deliberately not expressible — the deployment that had explicitly set `true` before this change also moved its preselect to perpetual.

## Consequences

- **Reach.** Fresh installs and deployments upgrading from pre-step-4 builds get the new default. A deployment that already started a step-4+ build keeps its persisted value: `ServerConfig.ResaveSettings` serializes every knob — defaults included — on the first start, pinning the then-current `false` in the config file.
- **Not retroactive.** The gate is consulted at create only: switching it off stops NEW no-expiration tokens; existing ones keep authenticating, and rotation preserves a perpetual expiry (`RotateToken` never reads the gate). The cleanup lever for inherited perpetual tokens is the admin revoke (per-token or emergency revoke-all).
- `MaxLifetime` still caps every finite lifetime — switching the gate off remains a real policy bound, with no year-9999 sidestep.
- **Wire encoding (known ambiguity, follow-up).** The perpetual choice is `ExpiresAtUtc: null`; a body that omits the field is indistinguishable from the explicit choice. The only sender today is the profile JS, which routes an incomplete form to a client-side validation error rather than sending null — "explicit" is a property of one JS file, not of the protocol. Tracked as a follow-up (explicit `NoExpiration` flag).

## Alternatives Considered

- **Keep the opt-in default:** rejected — wrong default for the credential's audience; the renewal-date failure mode is user-visible, the perpetual risk is operator-managed.
- **A separate preselect knob (e.g. `DefaultToNoExpiration`):** rejected — extra configuration surface for a distinction nobody asked for; the coupling is the simpler contract.
- **Sentinel `DefaultLifetime = 0` meaning "preselect no expiration":** rejected — overloads a `TimeSpan` with a magic value and contorts its startup validation.

## References

- Issue #1373, PR #1376
- `aicontext/features/server/api-tokens/feature.md` (issuance-side configuration)
- ADR-0002, ADR-0003 (sibling token decisions)
