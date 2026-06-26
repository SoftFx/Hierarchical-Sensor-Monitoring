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

Current: 78 important bug-class tests that expose P1/P2 bugs in either collector.

Shared conformance count:

- Before the second regression batch: 69 `.hsmtest` cases.
- After promoting shared semantic regressions: 89 `.hsmtest` cases.
- After representative duplicate last-value regression promotion: 90 `.hsmtest`
  cases.
- After six red/fix/green bug-class cycles: 97 `.hsmtest` cases.
- After the .NET-only collector hardening cycles: shared conformance remains
  97 `.hsmtest` cases, plus 9 focused managed regressions for APIs that do not
  yet have native/shared file scenarios.
- After the stability retry batch: shared conformance remains 97 `.hsmtest`
  cases, plus 21 focused managed regressions.

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
11. `native_create_rejects_null_server_address`
    - Found native collector creation accepted a null server endpoint.
12. `native_create_rejects_blank_server_address`
    - Found native collector creation accepted a blank server endpoint.
13. `native_create_rejects_null_access_key`
    - Found native collector creation accepted a null access key.
14. `native_create_rejects_blank_access_key`
    - Found native collector creation accepted a blank access key.
15. `native_slash_only_module_is_omitted_from_payload_path`
    - Found slash-only module prefixes could produce malformed payload paths.
16. `native_slash_only_computer_name_is_omitted_from_payload_path`
    - Found slash-only computer prefixes could produce malformed payload paths.
17. `native_slash_only_module_and_computer_name_are_omitted_from_payload_path`
    - Found combined slash-only prefixes could produce leading-slash paths.
18. `native_whitespace_only_path_is_rejected`
    - Found whitespace-only native sensor paths were accepted.
19. `native_instant_string_null_value_is_invalid_and_not_sent`
    - Found null native string values were silently converted to empty strings.
20. `native_last_value_string_null_default_is_invalid`
    - Found null native last-value string defaults were silently accepted.
21. `native_last_value_string_null_update_is_invalid_and_preserves_previous`
    - Found null native last-value string updates could overwrite the snapshot.
22. `native_json_escapes_control_chars_in_string_value`
    - Found raw control characters could make string-value JSON invalid.
23. `native_json_escapes_control_chars_in_comment`
    - Found raw control characters could make comment JSON invalid.
24. `native_json_escapes_control_chars_in_path`
    - Found raw control characters could make path JSON invalid.
25. `native_json_escapes_control_chars_in_options_path_prefix`
    - Found raw control characters in computer/module prefixes could poison
      payload paths.
26. `native_double_nan_is_rejected_and_not_sent`
    - Found native doubles could serialize `NaN` as invalid JSON.
27. `native_double_positive_infinity_is_rejected_and_not_sent`
    - Found native doubles could serialize positive infinity as invalid JSON.
28. `native_double_negative_infinity_is_rejected_and_not_sent`
    - Found native doubles could serialize negative infinity as invalid JSON.
29. `native_invalid_status_on_instant_value_is_rejected_and_not_sent`
    - Found invalid native status enum values could be sent.
30. `native_invalid_status_on_last_value_preserves_previous_snapshot`
    - Found invalid native status enum values could overwrite last-value state.

Promoted to shared conformance:

31. `double_nan_is_rejected`
32. `double_positive_infinity_is_rejected`
33. `double_negative_infinity_is_rejected`
34. `last_double_nan_default_is_rejected`
35. `last_double_positive_infinity_default_is_rejected`
36. `last_double_negative_infinity_default_is_rejected`
37. `last_double_nan_update_preserves_previous`
38. `last_double_positive_infinity_update_preserves_previous`
39. `last_double_negative_infinity_update_preserves_previous`
40. `int_invalid_status_is_rejected`
41. `bool_invalid_status_is_rejected`
42. `double_invalid_status_is_rejected`
43. `string_invalid_status_is_rejected`
44. `enum_invalid_status_is_rejected`
45. `last_int_invalid_status_preserves_previous`
46. `last_bool_invalid_status_preserves_previous`
47. `last_double_invalid_status_preserves_previous`
48. `last_string_invalid_status_preserves_previous`
49. `string_control_character_is_escaped`
50. `path_control_character_is_escaped`
51. `duplicate_last_int_while_running_does_not_emit_duplicate_default`
52. `last_sensor_release_does_not_flush`
53. `path_duplicate_separators_are_normalized`
54. `blank_computer_name_is_omitted_from_payload_path`
55. `string_null_value_is_rejected` / `last_string_null_default_is_rejected`
56. `blank_access_key_is_rejected`
57. `zero_port_is_rejected`
58. `SendFileAsync_uses_single_system_path_prefix`
    - Found managed `SendFileAsync` double-prefixed the computer/module path
      before creating the file sensor.
59. `SendFileAsync_rejects_invalid_status_without_sending_file`
    - Found managed file sending could accept and transmit an undefined
      `SensorStatus`.
60. `SendFileAsync_before_start_returns_false_without_sending_file`
    - Found file sending reported success before the collector could accept
      data, even though nothing was sent.
61. `Rate_sensor_rejects_invalid_status_updates`
    - Found monitoring rate updates could persist an undefined status into the
      next periodic payload.
62. `Double_bar_sensor_rejects_nan_values_without_sending_bar`
    - Found public double bar sensors could serialize non-finite values.
63. `Function_sensor_rejects_nan_values_without_sending_payload`
    - Found periodic function sensors could send non-finite return values.
64. `File_sensor_string_value_rejects_null_without_throwing_or_sending`
    - Found file sensor string values could throw on null input instead of
      rejecting it safely.
65. `Double_bar_sensor_rejects_negative_precision`
    - Found invalid bar precision was accepted at creation and could become a
      runtime send failure.
66. `Double_bar_sensor_rejects_inconsistent_partial_values_without_sending_bar`
    - Found public bar partial updates could send impossible min/max/range
      payloads.
67. `Transient_command_send_failure_retries_registration_command`
    - Found registration commands could be lost after one transient
      `SendCommandAsync` exception.
68. `Transient_file_send_failure_retries_file_payload`
    - Found accepted file payloads could be lost after one transient
      `SendFileAsync` exception.
69. `Failed_package_sending_info_retries_dequeued_values`
    - Found data values could be dropped when the HTTPS layer returned an
      explicit failed `PackageSendingInfo` instead of throwing.
70. `Failed_command_package_sending_info_retries_registration_command`
    - Found registration commands could be dropped when command sending returned
      an explicit failed `PackageSendingInfo`.
71. `Failed_file_package_sending_info_retries_file_payload`
    - Found accepted file payloads could be dropped when file sending returned
      an explicit failed `PackageSendingInfo`.
72. `Values_function_sensor_cache_stays_bounded_under_concurrent_producers`
    - Found values function sensors could observe more cached items than
      `MaxCacheSize` under concurrent producers.
73. `Accepted_file_payloads_are_flushed_when_stop_races_file_queue`
    - Found accepted file payloads could be discarded when stop raced the file
      queue before a bounded flush.
74. `Restart_timer_during_blocked_function_callback_does_not_overlap_callbacks`
    - Found `RestartTimer` could start overlapping periodic callbacks when the
      previous callback stayed blocked past the bounded stop wait.
75. `Accepted_registration_commands_are_flushed_when_stop_cancels_command_queue`
    - Found accepted registration commands could be discarded when stop canceled
      an in-flight command send before a bounded flush.
76. `Accepted_data_values_are_flushed_when_stop_cancels_in_flight_data_send`
    - Found accepted data values could be discarded when stop canceled an
      in-flight data send after the package had already been dequeued.
77. `Values_added_by_custom_stopping_task_are_flushed_or_rejected`
    - Found values accepted by `Stop(customStoppingTask)` could be stranded
      after queues stopped before the custom task finished producing data.
78. `Long_function_callback_released_after_data_flush_is_rejected_or_flushed_during_stop`
    - Found long-running function callbacks could enqueue values into an
      already-stopped data queue after bounded sensor stop and data flush.
