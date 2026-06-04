# Collector Review/Fix Loop - 2026-05-27

Branch: `codex/wiki-docs-ruleset`

Scope: `HSMDataCollector` queue lifecycle, channel-based queue processors, scheduler concurrency, adversarial tests, and test documentation.

Related PR: `SoftFx/Hierarchical-Sensor-Monitoring#1053`

## What changed

- Queue processors were migrated to `System.Threading.Channels`.
- `QueueProcessorBase.Start()` now reports whether the worker actually started; `DataProcessor.Start()` only marks the collector started after all required queues start.
- Failed restart rollback now stops already-started queues with `clearQueue: false`, so preserved packages are not lost during rollback.
- Data and priority processors skip empty packages and re-enqueue failed non-cancellation sends.
- Priority data retry now has cancellation-aware delay after failed sends to avoid a tight retry loop.
- Scheduler due tasks are dispatched independently through the thread pool, so one blocked user callback does not starve other due tasks in the same batch.
- `ScheduledTask.StopAsync(waitForCurrentRun: true)` intentionally uses a bounded wait for the current callback.

## Intentional synchronization decision

The scheduler does not wait forever for user callbacks during stop. A callback can ignore cancellation or block indefinitely, so stop waits briefly for normal in-flight work but remains bounded. This preserves collector shutdown behavior while still allowing short callbacks to finish before sensor disposal.

## Tests added or extended

- `Stop_with_priority_sender_that_ignores_cancellation_does_not_hang`
- `Stop_with_command_sender_that_ignores_cancellation_does_not_hang`
- `Stop_with_file_sender_that_ignores_cancellation_does_not_hang`
- `Creating_sensor_while_stop_is_in_progress_does_not_start_it`
- Scheduler tests now cover both blocked callbacks and short callback quiescence.

## Documentation updated

- `docs/test/CollectorAdversarialTests.md`
- `docs/test/CollectorTestCatalog.md`

Current documented collector test status:

- Passed: 62
- Skipped: 9
- Total: 71

## Verification

- `dotnet restore src/collector/HSMDataCollector.sln` passed.
- `dotnet build src/collector/HSMDataCollector.sln --no-restore` passed with existing warnings.
- Focused adversarial/timer test runs passed.
- Full collector test project run passed: 62 passed, 9 skipped, 71 total.
- `git diff --check` passed, with only line-ending warnings.

## Remaining integration note

The local branch is still behind `origin/codex/wiki-docs-ruleset` by 2 commits. Pull was not applied because local uncommitted changes overlap with remote changes in collector queue/scheduler files. Sergey integration-test changes were reviewed in a separate worktree and still need a clean merge/integration pass.
