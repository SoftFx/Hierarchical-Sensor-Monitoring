# ADR-0009: Bar StdDev is sent by the native collector only (exception to rules #9/#10)

**Status:** Accepted
**Date:** 2026-10-07
**Supersedes:** —

---

## Context

#1509 adds a per-bar population standard deviation (`StdDev`) so the sensor page can draw a mean ± σ band
and dashboards can plot σ. CLAUDE.md rule #9 requires a collector behavior change to land in both
collectors with a shared conformance scenario, and rule #10 requires a default sensor under one path to
behave identically in both collectors. The first implementation did exactly that (Welford moments with
the same operation order and rounding in `HSMDataCollector` and `src/native/collector`, byte-identical
wire). On 2026-10-07 the owner decided that only the native C++ collector supports σ; the managed .NET
collector does not.

## Decision

- **The native collector (0.11.0) sends `StdDev` on every bar; the managed collector does not.**
  Managed bars are byte-identical on the wire to what they were before #1509.
- The field lives on the server-facing DTOs `IntBarSensorValue` / `DoubleBarSensorValue`
  (`HSMSensorDataObjects` 3.2.0), **not** on `BarSensorValueBase<T>`: the managed collector's bar types
  (`IntMonitoringBar` / `DoubleMonitoringBar`) derive from the base, so they never acquire the property
  and their serialization is unchanged without any attribute or serializer change. System.Text.Json puts
  a property declared on the concrete DTO right after `Type`, and the native wire matches that position.
- The server treats a missing `StdDev` as **unknown** (no band, no σ line, empty CSV/Grafana cell) — the
  same state as a bar from an older collector.
- Conformance: the σ scenarios live in the native-only fixture
  `tests/conformance/collector/native/bar_stddev_contract.hsmtest`, which the managed driver's
  non-recursive discovery never reads; the managed driver marks `expect_bar_field … stddev`
  `CONFORMANCE-UNSUPPORTED` (#1529). `WireFormatGoldenLockTests` pins the divergence explicitly: native
  bytes = the DTO's bytes; managed bytes = the same minus the `StdDev` member. The differential fuzzer
  strips the native-only canonical `StdDev` field before comparing dumps.

## Consequences

- A host on the managed collector gets no σ band; switching to the native collector (or a later
  managed port) enables it with no server change.
- The two collectors are no longer byte-identical for bars. The difference is exactly one member, pinned
  from both sides, so any other drift still fails the golden lock and the fuzzer.
- Reversing the decision is tracked by #1529: port the Welford accumulation (operation order and
  rounding as documented in `aicontext/features/api/wire-contract/feature.md#bar-stddev-1509`), move the
  fixture back into the shared bar fixtures, drop the unsupported marker and the fuzzer strip, and pin
  identical bytes again. The managed collector needs no wire-format change for that: its bar types would
  declare `StdDev` themselves (or derive from the concrete DTOs).

## Alternatives Considered

- Keep σ in both collectors (the original #1509 implementation): overruled by the owner.
- Keep `StdDev` on `BarSensorValueBase<T>`: the managed bars would then emit `"StdDev":null`, a wire
  change for the collector that does not support the feature; hiding it would need a serializer
  attribute or modifier the netstandard2.0 DTO and the net6 target cannot share cleanly.
- Mark the shared σ cases unsupported in the managed driver: an unsupported verb fails the managed run,
  so the cases could not stay in the shared fixtures; a native-only fixture keeps both drivers green.

## References

- #1509 (feature), #1529 (managed StdDev, deferred), CLAUDE.md rules #9/#10
- `aicontext/features/api/wire-contract/feature.md#bar-stddev-1509`, `aicontext/features/server/bar-stddev/feature.md`
