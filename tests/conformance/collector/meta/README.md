# Conformance meta-suite — must-FAIL fixtures

Mutation tests for the conformance drivers themselves ("test the tests",
epic #1093 / workstream #1094). Every fixture in this directory contains a
deliberately WRONG expectation: a correct driver MUST fail the case. A driver
that passes any of these fixtures is broken — it is not actually evaluating
the assertion it claims to evaluate, and a green corpus run proves nothing.

Rules:

- **One case per fixture.** Drivers abort a fixture on the first failing step,
  so a second case would never be proven. Both runners enforce this.
- Each fixture targets exactly one assert-verb family (one mutation), so a
  regression in a single verb is pinpointed by a single meta test.
- A *crash* is not detection. The must-fail runners require the driver to
  REPORT a contract failure (an exception/assertion), not to die: the managed
  runner catches and demands an exception, the native runner wraps the
  contract run in try/catch inside the same process — a segfault still fails
  the meta test.
- These fixtures are intentionally invisible to the normal corpus discovery
  (the managed driver globs `tests/conformance/collector/*.hsmtest`
  non-recursively; the native driver registers fixtures explicitly). They run
  through dedicated must-fail entry points in both drivers.

Covered verb families: `expect_sent_count` (both polling-timeout and exact
mismatch directions), `expect_payload_contains`, `expect_payload_not_contains`,
`expect_bar_field`, `expect_bar_posts_sharing_open_time_at_least`, `expect_each_value_once`, `expect_payload_value_sequence`,
`expect_eventually_value_above`, `expect_no_new_payloads_for_ms`, the session
assertions `expect_session_ints_once` (a loss and a duplicate),
`expect_session_accepted_contains`, `expect_session_frames_fit`,
`expect_session_no_request_contains` and `expect_no_http_requests`
(they read the scripted session server's record, not the recording sender),
and the unknown-action guard (a typo'd verb must fail loudly, never skip silently).

The `meta_session_*` fixtures use the collector session verbs, which the
native driver does not implement yet (`CONFORMANCE-UNSUPPORTED`, #1668): they
come to the native CMake together with the session scenarios. Each of them
first sees a value or a registration delivered on the session, so that a
session that never came up is not what makes it fail.
