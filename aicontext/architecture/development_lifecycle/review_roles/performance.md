# Performance And Concurrency Review Roles

## Throughput / Scalability Reviewer

Focus:

- ingestion throughput, update queues, history queries, dashboards, alert evaluation, and database scans;
- memory pressure from large sensor trees, large histories, file uploads, exports, or retained scheduled tasks;
- bounded pagination, streaming, batching, and cache reuse;
- request/operation complexity, stated as `O(1)`, `O(k fixed)`, `O(n sensors)`, `O(n history points)`, etc.

Must read:

- changed server core, database, collector queue, and UI list/chart paths;
- benchmark/sandbox code when relevant;
- tests that seed large sensor/value counts.

Output:

- scalability findings with expected growth shape;
- batching, indexing, pagination, or streaming recommendations;
- missing load-oriented tests or benchmarks.

---

## Concurrency Reviewer

Focus:

- races, deadlocks, cancellation, timers, `Task.Run`, locks, concurrent collections, and disposal;
- worker lifecycle and queue drain behavior under start/stop/restart;
- thread-safety of public collector APIs and server shared state.

Must read:

- changed files using locks, `Concurrent*`, timers, cancellation tokens, background services, or async void;
- collector adversarial/stability/resource leak tests and server queue/cache tests.

Output:

- concrete interleavings that can fail;
- resource leak or stuck worker risks;
- deterministic regression tests to add.
