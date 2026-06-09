# Observability And Operations Review Roles

## Log Diagnostics Reviewer

Focus:

- NLog/logging quality in collector, server, ping module, and background services;
- whether important lifecycle, ingestion, alerting, notification, storage, and queue events are logged with stable context;
- whether repeated failures are deduplicated or rate-limited rather than flooding logs;
- whether expected failures are distinguishable from code defects.

Must read:

- changed logging, exception handling, retry, background worker, and notification code;
- `nlog.config`, `collector.nlog.config`, Docker/config files, and operational docs when touched;
- tests around failure paths when present.

Output:

- missing logs for operationally important events;
- over-logging or log-spam risks;
- unsafe sensitive data exposure;
- recommended log levels and tests for error paths.

---

## Release / Operations Reviewer

Focus:

- Docker, config, environment variables, startup/shutdown, deployment ordering, and rollback;
- compatibility between server, collector, database, wrapper, and module versions;
- file paths, permissions, native dependencies, and platform assumptions;
- whether partial deployment leaves components incompatible.

Must read:

- `docker-compose.yml`, Dockerfiles, config classes, native library paths, package/project files, and release notes when touched;
- startup code and hosted/background services affected by the PR.

Output:

- release blockers and safe rollout notes;
- rollback caveats and compatibility checks;
- smoke-test commands or environment checks to run before handoff.
