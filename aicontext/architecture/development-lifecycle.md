# Development Lifecycle

> Owner: shared | Last reviewed: 2026-05-26 | Canonical: yes

## Workflow

### 1. Before coding

1.1. Read `AGENTS.md` and relevant `aicontext/features/` docs.
1.2. If changing DataCollector public API: check backward compatibility with net472.
1.3. If fixing a bug: write a failing test first if possible.

### 2. Implementation

2.1. Write code following `AGENTS.md` Architecture Rules.
2.2. Write or update tests.
2.3. Run relevant test suite locally.
2.4. Update `aicontext/` docs from the actual diff when behavior or architecture changes.

### 3. Review & Handoff

3.1. Self-review the diff for: exception leaks, resource leaks, thread safety, backward compatibility.
3.2. Bump only the relevant component version when preparing a release/package or when the task asks for it.
3.3. Commit with descriptive message in imperative mood.
3.4. Push branch and create PR.
3.5. No auto-merge: human reviews and merges.

## Code Review Checklist (DataCollector)

- [ ] All exceptions caught in periodic callbacks (no unobserved Task faults)
- [ ] `IDisposable` resources disposed (HttpResponseMessage, HttpContent, Process, ServiceController)
- [ ] Thread-safe access to shared state (Interlocked/Volatile/lock)
- [ ] No `ConcurrentQueue.Count` in hot paths (use tracked `_queueCount` with Interlocked)
- [ ] `Dispose()` is idempotent and safe from any state
- [ ] No data enqueued while collector is stopped (`IsStarted` guard)
- [ ] Lifecycle events don't propagate subscriber exceptions
- [ ] Options validated early (constructor or `Validate()`)
- [ ] No unbounded caches or dictionaries (enforce max size)

## Code Review Checklist (Server)

- [ ] Sensor model initialization order is correct (no reading LastValue before Initialize)
- [ ] TTL/policy changes don't create race conditions
- [ ] Background services handle cancellation gracefully
- [ ] LevelDB operations don't hold locks longer than necessary
