# Testing Strategy

> Owner: shared | Last reviewed: 2026-11-26 | Canonical: yes

## Test Projects

| Project | Framework | Purpose |
|---|---|---|
| `HSMDataCollector.Tests` | xUnit | Collector unit, stress, chaos, adversarial, resource leak tests |
| `HSMServer.Core.Tests` | xUnit | Server core logic: converters, sensor model, cache, updates queue |
| `HSMDatabase.LevelDB.Tests` | xUnit | LevelDB database operations: journal, sensor values |
| `Autotests` | Playwright (TypeScript) | E2E browser tests: auth, registration, environment management |

## Test Layers

### 1. Collector Unit & Stress Tests

Location: `src/collector/HSMDataCollector.Tests/`

Categories:
- **Unit tests** (`DefaultSensorsTests`) — sensor creation, value generation, basic lifecycle
- **Stress tests** (`CollectorStressTests`, `CollectorTimerStressTests`) — high concurrency, CPU regression, many timers
- **Chaos tests** (`CollectorTransportChaosTests`) — flaky server, no-accept, mixed transport failures, soak tests
- **Adversarial tests** (`CollectorAdversarialTests`) — blocked callbacks, malicious inputs, edge cases
- **Resource leak tests** (`CollectorResourceLeakTests`) — memory, handle, thread leaks under repeated start/stop cycles

These tests use a mock `IDataSender` to isolate collector logic from the real server.

Key invariants tested:
- Collector survives any combination of start/stop/dispose calls
- No data sent while collector is stopped
- Resources are released after dispose (handles, threads, memory)
- Timer callbacks don't block collector stop
- Exception in one sensor doesn't affect others

### 2. Server Unit Tests

Location: `src/tests/HSMServer.Core.Tests/`

Categories:
- Converter tests — DTO mapping
- Sensor model tests — `BaseSensorModel<T>` behavior, TTL, policies
- Tree values cache tests — in-memory tree operations
- Updates queue tests — Channel-based processing
- Database tests — LevelDB operations

#### Parallelism and the database test classes (#1943)

xUnit runs every test class as its own collection, and the collections in parallel; the tests of one
class run in order. The classes on `DatabaseCoreTestsBase<T>` / `MonitoringCoreTestsBase<T>` (also used
by `HSMDatabase.LevelDB.Tests`) open real LevelDB databases and still run in parallel, because nothing
of that state is shared between classes:

- each class fixture (`DatabaseFixture`) has its own folder, `TestDB_<name>_<instance number>`: several
  classes share one fixture type, and xUnit builds a fixture per class;
- each test's databases carry a process-wide number in their names (`DatabaseCoreManager`);
- each class has its own `DatabaseRegisterFixture` (a class fixture, through the non-generic
  `DatabaseTestClass` root), which closes the class's databases and removes its folder when the class
  ends. The removal is one attempt and a failure is an error: `DatabaseCore.Dispose` closes every
  database it opened, so a folder that cannot be removed means something still holds one of its files
  (a database the test opened past the core, a handle leaked by the product). The fixture's `Dispose`
  then throws, and xUnit reports a "Test Class Cleanup Failure" against the tests of that class only,
  which turns the run red. Until #1943 `DatabaseCore.Dispose` left the Dashboards and ChangeSets
  databases open and the removal was silent; `DatabaseCoreDisposeTests` pins the fix.

The test host starts with at least 128 worker and I/O pool threads (`Infrastructure/TestThreadPool.cs`, a
module initializer): when dozens of classes start at once and block pool threads, a pool that grows by
injection lets the product's bounded waits on its background loops (the security-event sink's 3 s drain,
`UpdatesQueue.DisposeAsync`'s 5 s) expire.

There is no shared "Database collection" any more; do not add one back to a database class. Before
adding a static field, an environment variable, a fixed port or a fixed file path to such a class, put
the class in an exclusive collection instead.

The serial (exclusive, `DisableParallelization = true`) collections, which run alone after the parallel
ones:

| Collection | Classes | Why |
|---|---|---|
| `NLog isolated` | `AlertScheduleProviderMissingIdLoggingTests`, `LoggingExceptionMiddlewareTests`, `ChannelTestSendResultTests`, `TelegramBotStartFailureLoggingTests`, `TableFeedSensorsServiceTests`, `DumpPasswordServiceLoggingTests` | They swap the process-global NLog configuration. |
| `Process-wide measurement` | `CollectorSessionTests` | `IdleSession_CostsTensOfKilobytes` reads the process-wide managed heap, and a shutdown test bounds a wall-clock time. |
| `Large file store kill (alone)` (`HSMDatabase.LevelDB.Tests`) | `LargeFileStoreKillTests` | It starts and kills a child process with redirected output, which inherits every open (inheritable) LevelDB file handle of the test host. |

Measured locally (24 cores, other suites running on the machine; #1943): `HSMServer.Core.Tests` went
from 23 m 40 s to 1 m 43 s with the same 3,641 passed and 5 skipped tests. The longest class,
`TableMergeJobTests` (about 80 s under load), now sets the floor of the run.
`HSMDatabase.LevelDB.Tests` (about 4 min) is bound by single classes, not by the collection:
`SealedWeekDifferentialTests` and `HistorySealingJobTests` take about 4 min each, their tests in order
inside the class; splitting them into several classes is what would shorten that run.

#### On pull requests

`.github/workflows/server-tests.yml` runs every test project of `HSMServer.sln` (`HSMServer.Core.Tests`,
`HSMDatabase.LevelDB.Tests`, `HSMDatabase.SealedHistory.Tests`, `HSMTableBlocks.Tests`) on every pull
request and on `workflow_dispatch`, on `windows-latest`, with the same restore and
`dotnet test HSMServer.sln --no-restore -c Release` as the `build` job of `server-build.yml` (which keeps
its own run on a push to master/develop). It has no paths filter, so it can become a required check; a
new push to the pull request cancels the superseded run; the trx files are uploaded as the
`server-test-results` artifact when the run fails. A test project added to `HSMServer.sln` runs there
with no change to the workflow.

### 3. Database Tests

Location: `src/tests/HSMDatabase.LevelDB.Tests/`

Tests LevelDB read/write operations for journal entries and sensor values.

### 4. E2E Tests (Playwright)

Location: `src/tests/Autotests/`

Browser-level tests using Playwright (TypeScript):
- Auth flow
- User registration
- Environment management
- The page of a table sensor (`home/table_sensor_page.spec.ts`, #1847): the rows it draws are sent by
  `tools/TableSeeder`, a .NET 8 console client of the collector session's table connection built on the repo's
  `HSMTableBlocks` (one block, waits for the ack, exits non-zero with the reason on a refusal; `--insecure` accepts the
  test server's self-signed certificate; the access key comes from `--key` or, kept off the command line, from
  `HSM_TABLE_SEEDER_KEY`, which is how the spec passes it). The spec runs it with `dotnet run -c Release`, so the machine that runs the
  suite needs the .NET 8 SDK; the e2e workflow (`tests.yml`) builds the tool once before the specs run.

## Running Tests

```bash
# Collector tests
dotnet test src/collector/HSMDataCollector.Tests/

# Server core tests
dotnet test src/tests/HSMServer.Core.Tests/

# Database tests
dotnet test src/tests/HSMDatabase.LevelDB.Tests/

# E2E (requires running HSMServer; the base URL defaults to https://localhost:443)
cd src/tests/Autotests && npx playwright test
# against another server, one spec (the setup project runs first)
PLAYWRIGHT_TEST_BASE_URL=https://localhost:44990 CI=1 npx playwright test --project=chromium home/table_sensor_page.spec.ts
```

## Test Writing Guidelines

- Use `[Fact]` for single-case tests, `[Theory]` for parameterized
- Collector tests: use `IDataSender` mock, never real HTTP
- Stress tests: assert resource bounds (memory, handles, threads) not just correctness
- Chaos tests: simulate real network conditions (delays, drops, partial failures)
- Always test dispose/stop from any state, not just Running
- Exception tests: verify exceptions are caught internally, not propagated to caller

## Principles

- Tests should prove behavior and regressions, not only exercise code paths.
- Prefer deterministic signals over wall-clock sleeps for concurrency and timers.
- Keep tests close to the layer where the risk lives.
- Use stress/soak tests for confidence, not as the only regression guard.

## Layer Selection

- Collector lifecycle/scheduler/queue behavior: collector unit or adversarial tests.
- Server domain/cache/update behavior: server core tests.
- Storage key/serialization/history behavior: LevelDB tests.
- Rendered site workflows: Playwright tests.
- Public API/DTO compatibility: serialization or integration tests near the contract.

## Timing And Concurrency

- Use `TaskCompletionSource`, events, fake clocks, bounded waits, or explicit queue drains.
- Avoid tests that assert exact timing on loaded CI runners.
- For disposal/cancellation changes, verify no late callbacks, unobserved exceptions, or retained workers when feasible.

## CI Lane Gating (#1278)

The collector/native lanes are required checks, so their `pull_request:` triggers carry no
`paths:` filter (a filtered required check never reports on an unrelated PR and blocks the merge)
and their jobs carry no change-based job-level `if:` (a matrix job skipped at job level reports
under its unexpanded name, so the per-OS required checks stay pending).
Instead `native-collector-conformance.yml`, `collector-unit-tests.yml` and
`collector-integration-tests.yml` gate at the step level: every gated job checks out with
`fetch-depth: 2`, runs `.github/actions/detect-lane-changes` (diff of the PR merge commit against
its base parent, `HEAD^1..HEAD`, matched against the workflow's `LANE_PATHS`), skips every
build/test step when it reports `run=false`, and ends with `.github/actions/lane-summary`, which
writes "Lane RAN" or "Lane SKIPPED" plus the reason to the log and the job summary. A docs-only PR
therefore gets these checks green in seconds; a PR touching a lane's paths runs the full matrix.

- `LANE_PATHS` per workflow: native — `src/native/**`, `tests/conformance/**`,
  `docs/native-collector/**`, `ports/**`, `versions/**`, root `conanfile*` / `vcpkg*.json`,
  `scripts/conformance-*`, `.gitattributes`; collector unit — `src/collector/**`, `src/api/**`,
  `tests/conformance/**`, `src/Directory.Build.*`, `global.json`, `.gitattributes`; collector
  integration — the collector unit set (incl. `tests/conformance/**`: `WireFormatGoldenLockTests`
  reads the golden files there byte for byte, and `.gitattributes` fixes their line endings) plus
  `src/server/**`, `src/database/**`, `src/lib/**` (the lane builds the server image from branch
  source) and `src/native/collector/**` (`NativeTableSensorEndToEndTests` builds the native
  collector's table driver from branch source and runs it against that server). Each also lists its
  own workflow file.
- Always the full lane: any change under `.github/**`, every `push` / `workflow_dispatch` /
  `schedule` run, and any doubt (HEAD not a two-parent merge commit, `git diff` failure) —
  detection fails open.
- Not gated on purpose: `format-check` (seconds) and `checklist-gate` (text checks that guard
  docs-only PRs too).
- When a lane starts consuming a new input (a new project reference, a script, a root config
  file), add it to that workflow's `LANE_PATHS` in the same PR, or PRs touching only that input
  will skip the lane.

## Handoff

Every review/fix handoff should include:

- commands run;
- pass/fail/skip counts;
- skipped tests relevant to the change;
- any tests not run and why.
