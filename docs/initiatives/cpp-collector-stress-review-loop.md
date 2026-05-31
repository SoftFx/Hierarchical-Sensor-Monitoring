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

## Regression Counter

Target: 10 important tests that expose P1/P2 bugs in either collector.

Completed:

1. `mixed_duplicate_type_registration_stress_rejects_conflicts`
   - Found native path-only identity reuse and managed `int`/`enum` identity
     ambiguity.
2. `start_twice_is_noop`
   - Found native `Start` behavior drift from managed idempotent `Start`.
3. `instant_then_last_same_path_is_rejected`
   - Found managed instant/last-value identity ambiguity.
4. `last_then_instant_same_path_is_rejected`
   - Found the reverse managed instant/last-value identity ambiguity.
5. `native_invalid_argument_clears_out_params`
   - Found stale C ABI out-parameters after invalid calls.
6. `native_add_after_collector_destroy_is_rejected`
   - Found last-value sensors accepting writes after collector destruction.
7. `native_sent_json_failure_reports_fresh_error`
   - Found stale `last_error` after missing sent-json lookup.
8. `native_wrapper_sent_json_missing_throws_message`
   - Found the C++ wrapper could throw empty or stale messages for missing
     sent-json lookup.
9. `path_leading_trailing_slashes_are_normalized`
   - Found native path normalization drift at slash boundaries.
10. `slash_only_path_is_rejected`
    - Found that slash-only paths needed an explicit shared invalid-path
      invariant before porting.
