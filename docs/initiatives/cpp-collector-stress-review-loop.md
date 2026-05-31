# C++ Collector Stress Review Loop

This loop turns focused review hypotheses into bounded shared stress tests for
the .NET collector and the native C++ port.

## Roles

- Native lifecycle/concurrency explorer: looks for races and lifecycle edge
  cases in native start/stop, add, create, destroy, and last-value flush paths.
- Managed/native contract-gap explorer: compares .NET behavior, native behavior,
  and shared fixtures to find missing or accidental conformance coverage.
- C ABI/native API safety explorer: reviews handle ownership, null arguments,
  out-parameter behavior, wrapper exceptions, error reporting, and returned
  pointer lifetime.

## Loop

1. Launch the three explorers in parallel.
2. Collect findings and discard anything that is speculative without a bounded
   test shape.
3. Classify every finding as one of: shared collector invariant, .NET refactor,
   native implementation gap, native-only C ABI detail, or deferred risk.
4. Prefer shared `.hsmtest` coverage when the behavior is part of collector
   semantics; use native-only tests for C ABI ownership and pointer-lifetime
   details.
5. When a finding describes shared semantics, keep the .NET collector and C++
   collector behavior aligned. Refactor .NET internals at the same time when an
   implicit behavior should become an explicit invariant for the port.
6. Add the smallest stress/conformance case that fails before the fix.
7. Fix the collector or test adapter with the smallest compatible change.
8. Run `scripts/test-conformance.ps1`, then native CTest and the .NET collector
   test project when shared behavior changed.
9. Commit and push one focused batch.

## Current Explorer Batch

- Native lifecycle/concurrency: start/stop races, last-value flush, duplicate
  registration, destroy/release interactions.
- Managed/native contract gaps: enum/int identity, path normalization, comment
  trimming, stopped values, duplicate handles, queue/cardinality limits.
- C ABI/API safety: null out parameters, stale handles after failed create,
  `last_error` behavior, `sent_json` pointer lifetime, C++ wrapper exceptions.
