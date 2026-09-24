# ADR-0007: Distribute Caddy with DNS challenge modules as a pinned image

**Status:** Accepted
**Date:** 2026-09-24
**Supersedes:** —

---

## Context

The supported Docker Compose deployment terminates TLS in Caddy. DNS-01 is needed for Let's Encrypt issuance when inbound HTTP validation cannot reach the host, including installations behind CGNAT. The standard Caddy image does not contain the Cloudflare and dynv6 DNS provider modules, and requiring each operator to build a custom image adds setup steps and version drift.

## Decision

Publish and consume the ready-made, pinned image hsmonitoring/hsm-caddy:2.11.4-1. It contains Caddy 2.11.4, Cloudflare DNS module v0.2.4, and the dynv6 module built from commit 71fad600afb29911aa04cbfd99f7d5d878c3bc48. The trusted-master CI workflow publishes the image after its checks succeed; pull requests do not publish it. A new tag is usable after merge and the first successful master publication.

Expose DNS-01 through HSM_CERTIFICATE=letsencrypt-dns, HSM_DNS_PROVIDER=cloudflare|dynv6, and the matching CF_API_TOKEN or DYNV6_API_TOKEN. Keep letsencrypt as an alias for letsencrypt-http; retain HTTP ACME, self-signed, custom PEM, and direct HSM PFX deployment. Do not fall back automatically between certificate sources. DNS-01 validates control of the domain but does not create a route through CGNAT or otherwise enable external access.

## Consequences

- Standard deployments pull a ready-to-use image; operators do not install a local Caddy build toolchain.
- Provider credentials remain environment variables and must be treated as secrets. Compose-rendered configuration may print interpolated values.
- The image tag is pinned. Updating Caddy or either DNS module requires publishing and selecting a new tag.
- Trusted certificates can be issued using DNS validation without inbound port 80, while A/AAAA records and network routing remain operator-managed.
- First-time availability of a new tag depends on a successful trusted-master workflow after merge.

## Alternatives Considered

- Ask operators to build the image locally: rejected because it adds a toolchain and makes deployments vary by operator.
- Use HTTP-only ACME: does not work where external validation requests cannot reach the host.
- Select a fallback certificate automatically: rejected because issuance failures should remain visible and must not silently change the trust model.
