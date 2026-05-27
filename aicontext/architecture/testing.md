# Testing Strategy

> Owner: shared | Last reviewed: 2026-05-26 | Canonical: yes

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

### 3. Database Tests

Location: `src/tests/HSMDatabase.LevelDB.Tests/`

Tests LevelDB read/write operations for journal entries and sensor values.

### 4. E2E Tests (Playwright)

Location: `src/tests/Autotests/`

Browser-level tests using Playwright (TypeScript):
- Auth flow
- User registration
- Environment management

## Running Tests

```bash
# Collector tests
dotnet test src/collector/HSMDataCollector.Tests/

# Server core tests
dotnet test src/tests/HSMServer.Core.Tests/

# Database tests
dotnet test src/tests/HSMDatabase.LevelDB.Tests/

# E2E (requires running HSMServer)
cd src/tests/Autotests && npx playwright test
```

## Test Writing Guidelines

- Use `[Fact]` for single-case tests, `[Theory]` for parameterized
- Collector tests: use `IDataSender` mock, never real HTTP
- Stress tests: assert resource bounds (memory, handles, threads) not just correctness
- Chaos tests: simulate real network conditions (delays, drops, partial failures)
- Always test dispose/stop from any state, not just Running
- Exception tests: verify exceptions are caught internally, not propagated to caller
