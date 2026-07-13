# HSM Native Collector — Codex Instructions

Directory-scoped rules for `src/native/collector/` (the native C++ HSM collector + its C ABI). These
supplement the root [`AGENTS.md`](../../../AGENTS.md); where a rule here refines the root, the more
specific rule wins. Sibling: [`../../agent/AGENTS.md`](../../agent/AGENTS.md) (the agent that embeds this
collector).

## Versioning — the native collector's own version

The native collector has **one** version: `HSM_COLLECTOR_VERSION_{MAJOR,MINOR,PATCH}` in
[`include/hsm_collector/hsm_collector.h`](include/hsm_collector/hsm_collector.h) (keep `CMakeLists.txt`
`project(HsmCollectorNative VERSION x.y.z)` in sync — it feeds the `find_package(hsm_collector x.y)`
compatibility check, so a drift silently lets an incompatible consumer link). It is **INDEPENDENT** of
the managed C# collector, the host application (e.g. the agent's `HsmAgent VERSION`), and the server —
each is a distinct product with its own version. It is also the value the native collector reports as
the `.module/Collector version` sensor (`HSM_COLLECTOR_VERSION_STRING`, the `MAJOR.MINOR.PATCH` form).

**Bump it in the same PR as the change, per semver:**
- **MAJOR** — a breaking ABI change (reorder/remove a struct field, change a function's semantics).
- **MINOR** — additive, backward-compatible ABI growth (a new exported `hsm_*` function, an appended
  struct field).
- **PATCH** — a backward-compatible behavior/logic change that does **not** touch the ABI (e.g. a
  logging change).

Do **not** pin it to the managed C# collector (`HSMDataCollector.csproj <Version>`) — they are two
separate products and are allowed to diverge. Conformance compares the two collectors' wire
byte-for-byte but does **not** compare the `.module/Collector version` *value*, so a diverging version
is safe.

Full map of HSM's versions:
[`aicontext/features/core/versioning/feature.md`](../../../aicontext/features/core/versioning/feature.md).
