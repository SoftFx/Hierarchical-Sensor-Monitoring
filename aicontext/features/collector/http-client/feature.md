# Feature: HTTP Client

> Owner: collector | Last reviewed: 2026-06-10 | Canonical: yes
> Scope: Collector - HTTPS transport with Polly retry for sending sensor data

---

## Description

`HsmHttpsClient` wraps `System.Net.Http.HttpClient` with Polly retry pipelines for sending sensor data to HSMServer. Each value type (data, priority, file, command) has its own request handler with a separate retry pipeline. Wire shapes and endpoint payloads: `../../api/wire-contract/feature.md`.

---

## Business Rules / Invariants

- Default scheme is HTTPS; an explicit `http://` in `ServerAddress` requires `AllowPlaintextTransport=true`, otherwise upgraded to HTTPS.
- `AllowUntrustedServerCertificate` must be explicitly set to `true` to bypass TLS validation. Default is `false` (secure by default).
- Auth headers on every request: `Key: <AccessKey>`, `ClientName: <ClientName>`.
- `RequestTimeout` (default 30s) is set on `HttpClient.Timeout`.
- `HttpResponseMessage` is always disposed after use (`using` blocks).
- `HttpContent` is disposed after sending (in `ExecutePipelineAsync`).
- Polly retry per handler: data/priority/file — 10 attempts, exponential backoff 1 s → 2 min; commands — `int.MaxValue` attempts, linear backoff.
- `CancelPendingRequests()` (`ICancelableDataSender`, called by collector Stop) cancels the shared in-flight token and installs a fresh one. It must **NOT** dispose the HttpClient — disposal during a graceful stop converted remaining flush sends into `ObjectDisposedException` data loss (PR #1080 finding #7; regression-tested in `HsmHttpsClientCancellationTests`).
- `PackageSendingInfo` captures content size (chars), success flag, and error string (`"Code: {status}. {content}"` or exception message) for self-diagnostics.
- JSON: System.Text.Json, `AllowNamedFloatingPointLiterals`, runtime-polymorphic write via `JsonRequestConverter`.
- On send failure, error is logged with payload byte count (not payload content) to prevent sensitive data in logs.
- `Dispose()` is idempotent (guarded by `_disposed` flag). Disposes all handlers, token source, and HttpClient.
- `InnerException?.Message` uses null-conditional to avoid NRE when InnerException is null.

---

## Key Files

| File | Purpose |
|---|---|
| `Client/HttpsClient/HsmHttpsClient.cs` | HttpClient lifecycle, dispose, test connection |
| `Client/HttpsClient/Endpoints.cs` | URL construction from CollectorOptions |
| `Client/HttpsClient/RequestHandlers/BaseHandlers.cs` | Polly pipeline, send logic, error logging |

---

## Endpoints

All under `https://{server}:{port}/api/sensors/`:

| Endpoint | Purpose |
|---|---|
| `bool`, `int`, `double`, `string`, `timespan`, `version`, `rate`, `enum` | Typed sensor values |
| `intBar`, `doubleBar` | Bar sensor values |
| `file` | File sensor values |
| `list` | Batch sensor values (polymorphic, `"type"` discriminator) |
| `commands` | Command batches (per-key error dictionary in response) |
| `testConnection` | Connectivity check (GET) |
| `addOrUpdate` | Sensor registration/update |

---

## Known Issues / Limitations

- Polly retry does not configure `ShouldHandle` for HTTP status codes. Only transport-level exceptions trigger retries. HTTP 4xx/5xx responses are accepted as "successful" by Polly and the data is silently lost.
- No circuit breaker pattern. If the server is down for extended periods, each batch attempt will exhaust all retry attempts before failing.
