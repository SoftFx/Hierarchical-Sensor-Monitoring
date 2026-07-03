# Feature: Versioning

> Owner: shared | Last reviewed: 2026-07-03 | Canonical: yes
> Scope: The independent version numbers across HSM — what each is, and when to bump it.

---

## Overview

HSM carries several **independent** version numbers. They are not tied to each other — each tracks a
different thing, so bumping one does not imply bumping another. This is the map; the authoritative
bump rules live in the directory `AGENTS.md` files.

## The versions

| Version | Where | What it tracks |
|---|---|---|
| **HSM Agent** | `src/agent/CMakeLists.txt` → `project(HsmAgent VERSION …)` (`HSM_AGENT_VERSION`) | The self-update **delivery key**: the server delivers a build only when its version is *strictly greater* than the running agent's. |
| **Native collector — library** | `src/native/collector/include/hsm_collector/hsm_collector.h` → `HSM_COLLECTOR_VERSION_{MAJOR,MINOR,PATCH}` (mirror in `CMakeLists.txt project VERSION`) | The C++ collector's own semver. Feeds `find_package(hsm_collector x.y)` + `hsm_collector_version()`. **Not** reported to the server. |
| **Collector product** | `HSM_COLLECTOR_PRODUCT_VERSION` (native) **and** `src/collector/HSMDataCollector/HSMDataCollector.csproj <Version>` (managed) | The value of the `.module/Collector version` sensor — the shared **HSMDataCollector product** number. Native and managed are two language ports of one product, so they carry the **same** number (kept in lockstep; conformance compares them byte-identically). |
| **Shared API DTO** | `src/api/HSMSensorDataObjects/HSMSensorDataObjects.csproj <Version>` | The wire-contract package shared by server, collector, and wrappers. |
| **Server** | `src/server/HSMServer/HSMServer.csproj <Version>` | The HSM server application. |

## When to bump

- **Changed the agent binary** — including a native-collector change compiled into it → bump the
  **Agent** version (patch). Otherwise self-update cannot deliver it. → `src/agent/AGENTS.md`
- **Native collector — ABI grew**: added/changed an exported `hsm_*` function or appended a struct
  field → **MINOR** of the collector library version.
- **Native collector — breaking ABI**: removed/reordered a field or changed a function's semantics →
  **MAJOR**.
- **Native collector — behavior/logic fix that does NOT touch the ABI** (e.g. a logging change) →
  **PATCH** of the collector library version. → `src/native/collector/AGENTS.md`
- **DataCollector product release** → bump the **product** version on **both** native
  (`HSM_COLLECTOR_PRODUCT_VERSION`) and managed (`.csproj`) in lockstep. A one-language behavior fix
  does not move it on its own. → root `AGENTS.md` "Versioning"
- **Server / API-DTO release** → bump that `.csproj`. → root `AGENTS.md` "Versioning"

## Don't conflate the two collector numbers

The **library** version (`HSM_COLLECTOR_VERSION`, `0.x`) tracks the C++ ABI/build. The **product**
version (`.module/Collector version`, `3.4.x`, shared with the managed collector) is the release
identity reported to the server. They are different numbers for different purposes.
