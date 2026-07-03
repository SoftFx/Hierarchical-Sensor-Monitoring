# Feature: Versioning

> Owner: shared | Last reviewed: 2026-07-03 | Canonical: yes
> Scope: The independent version numbers across HSM — what each is, and when to bump it.

---

## Overview

HSM carries **four independent** version numbers, **one per product**. They are not tied to each other:
each tracks a different product, is allowed to diverge, and bumping one never implies bumping another.
Authoritative bump rules live in the directory `AGENTS.md` files.

## The four versions

| Product | Version (where) | When to bump |
|---|---|---|
| **Native (C++) collector** | `HSM_COLLECTOR_VERSION_{MAJOR,MINOR,PATCH}` — `src/native/collector/include/hsm_collector/hsm_collector.h` (mirror in `CMakeLists.txt project VERSION`) | **MAJOR** breaking ABI · **MINOR** new exported `hsm_*` function / appended struct field · **PATCH** backward-compatible behavior/logic change, no ABI change (e.g. a logging fix). → `src/native/collector/AGENTS.md` |
| **Managed (C#) collector** | `src/collector/HSMDataCollector/HSMDataCollector.csproj <Version>` | At a DataCollector NuGet release. |
| **HSM site / server** | `src/server/HSMServer/HSMServer.csproj <Version>` | At a server release. |
| **Agent** | `src/agent/CMakeLists.txt` → `project(HsmAgent VERSION …)` (`HSM_AGENT_VERSION`) | On **any change to the shipped agent binary** — including a native-collector change compiled into it — else self-update can't deliver it (it ships a build only when its version is *strictly greater* than the running agent's). → `src/agent/AGENTS.md` |

## Key points

- The **C++ collector** and the **C# collector** are two separate products. Each reports **its own**
  version as the `.module/Collector version` sensor, and they may differ (C++ `0.x`, C# `3.4.x`). There
  is no shared "product version" tying them together. Conformance compares the two collectors' wire
  byte-for-byte but does **not** compare the version *value*, so the divergence is safe.
- The **agent** embeds the native collector but is its own product: a collector change bumps the
  collector version **and** (because the agent binary changed) the agent version — two independent
  bumps for two products.
