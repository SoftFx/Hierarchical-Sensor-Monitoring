# ADR-0023: A value the server already holds is a no-op when it comes again

**Status:** Accepted
**Date:** 2026-11-27
**Supersedes:** —

---

## Context

Delivery from a collector to the server is at-least-once, on HTTP and on the collector session alike (ADR-0017). A
request or a frame the server stored is sent again when the collector did not see its answer: a lost ack, a timeout,
a Polly retry after a response that never came, and every stop that cancels a send in flight, whose stop drain sends
that package again (`stop_drain_contract:stop_sends_again_the_package_it_cancelled_in_flight`, both collectors).

The server took such a repeat as new data (#1925). Its rows are keyed by sensor and time, so history got no extra row,
but everything else ran again for a frame `[28 @ t28, 29 @ t29]` sent twice:

- alert policies were evaluated again, and a notification was raised again for each repeated value;
- the EMA was applied a second time, and the older row was rewritten with an EMA taken from the newer value;
- with `AggregateValues`, the run's `AggregatedValuesCount` grew by one;
- a repeated older bar closed the bar in progress and took its place as the newest value;
- `OutOfOrderValuesStored`, `SameTickValuesSuperseded` and `AggregateSpanOverlapsSkipped` were counted, with Warn lines.

## Decision

The server takes a value whose time and content equal a value it already holds for that sensor as delivered, and
changes nothing for it: no row, no alert, no EMA, no aggregate count, no counter and no log line.

- **Content** is what a collector sends: type, status, comment and value; for a bar also its open and close time,
  count, min, max, mean, first and last value and standard deviation. What the server adds is not compared: the
  receiving and delivery stamps, the aggregate count and last update, the EMA.
- **Held** means: the sensor's newest value has that tick (compared in memory), or the row at that tick in the
  database (one read, only for a value older than the newest). An aggregated row stands for a run of equal values
  from its time to its last update, and a value equal to the run within that span is held, whether `AggregateValues`
  is still on or not.
- Another value, status or comment on a stored tick is not a repeat: it replaces the row as before, whichever client
  sent it. The same value at the same time from two clients is indistinguishable from a repeat and is taken once.
- Not covered: files (their content is not compared, a repeat rewrites its own row as before), a timeout marker, and
  the value updates an operator makes in the UI, which are deliberate.

## Consequences

- An at-least-once repeat is harmless on the server, whatever its source and whichever collector sent it. The
  collectors and the wire do not change, and the conformance contract of the stop drain stays as it is.
- A value older than the newest costs one database read before it is stored. That is the out-of-order path, which
  writes its row directly anyway; a backfill of older values pays one read per value.
- A repeat is counted nowhere. It is not lost data (rule 8 of `CLAUDE.md`): the server holds exactly that value.
- A bar partial older than the bar in progress that was not itself stored (an earlier partial of the current period
  sent again after a newer one) is not held anywhere, so it still replaces the partial in progress, as before.

## Alternatives Considered

- **A shorter window in the collectors**: wait at a stop for the ack of a frame already written before cancelling
  it. Both collectors, their conformance contract and ADR-0017 change, and it narrows the window only: a lost ack
  still repeats the frame.
- **An idempotency key per frame** (a producer and a sequence, as table blocks have, ADR-0021): exact, but a protocol
  change in both collectors and the server, and it covers the session only, not HTTP.

## References

- #1925 (the duplicate delivery, item 2), #1978 (its diagnosis)
- ADR-0017 (delivery stays at-least-once), ADR-0021 (the sequence of table blocks)
- `aicontext/features/api/wire-contract/feature.md` (batch storage semantics)
