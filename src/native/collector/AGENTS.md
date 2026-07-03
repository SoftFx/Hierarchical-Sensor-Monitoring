# HSM Native Collector — Codex Instructions

Directory-scoped rules for `src/native/collector/` (the native C++ HSM collector + its C ABI). These
supplement the root [`AGENTS.md`](../../../AGENTS.md); where a rule here refines the root, the more
specific rule wins. Sibling: [`../../agent/AGENTS.md`](../../agent/AGENTS.md) (the agent that embeds this
collector).

## Versioning — two numbers, do not confuse them

Both live in [`include/hsm_collector/hsm_collector.h`](include/hsm_collector/hsm_collector.h) and are
**distinct from any host application's version** (e.g. the agent's `HsmAgent VERSION`).

### 1. C ABI version `HSM_COLLECTOR_VERSION_*` — bump it when the C ABI grows

- **Rule:** whenever a PR **adds or changes an exported `hsm_*` C ABI function, or appends a struct
  field**, bump this version **in the same PR** — `MINOR` for additive, backward-compatible growth;
  `MAJOR` for any breaking change (reorder/remove a field, change a function's semantics). Policy is
  stated in the header and `docs/native-collector-c-abi.md`.
- **Keep `CMakeLists.txt` `project(HsmCollectorNative VERSION x.y.z)` in sync** with the header macros —
  it feeds the `find_package(hsm_collector x.y)` compatibility check, so a drift silently lets an
  incompatible consumer link.
- This is the C++/native code's own version. **If you touch the native/C-ABI code in a way that changes
  what a consumer can call or observe, this number moves.**

### 2. Product version `HSM_COLLECTOR_PRODUCT_VERSION` — pinned to the managed collector

- This is the `.module/Collector version` sensor value — the shared **HSMDataCollector product** number.
- **Keep it byte-identical to the managed collector** (`src/collector/HSMDataCollector/HSMDataCollector.csproj`
  `<Version>`) so the native port and the C# collector report the same product version. **Do NOT bump it
  native-only** — that diverges two collectors meant to report one product version.
- It moves only at a **DataCollector product release**, in lockstep on BOTH sides. A native-only parity
  change (native catching up to existing managed behavior) does not move it.

**Precedent (#1221):** the built-in file logger added the C ABI function `hsm_collector_enable_file_logging`
but did **not** bump `HSM_COLLECTOR_VERSION` — an ABI-hygiene miss (a new function owes a `MINOR` bump).
It correctly left `HSM_COLLECTOR_PRODUCT_VERSION` at `3.4.12` (native parity with the managed collector,
which had no release). The fix bumped the ABI to `0.6.0` and resynced `CMakeLists.txt` (which had also
drifted to `0.4.0`).
