# Feature: HTTP Client

> Owner: collector | Last reviewed: 2026-05-26 | Canonical: yes
> Scope: Collector - HTTPS transport with Polly retry for sending sensor data

---

## Description

`HsmHttpsClient` wraps `System.Net.Http.HttpClient` with Polly retry pipelines for sending sensor data to HSMServer. Each value type (data, priority, file, command) has its own request handler with a separate retry pipeline.

---

## Business Rules / Invariants

- Default scheme is HTTPS. Users can override by providing an explicit scheme in `ServerAddress` (e.g., `http://...`).
- `AllowUntrustedServerCertificate` must be explicitly set to `true` to bypass TLS validation. Default is `false` (secure by default).
- `RequestTimeout` (default 30s) is set on `HttpClient.Timeout`.
- `HttpResponseMessage` is always disposed after use (`using` blocks).
- `HttpContent` is disposed after sending (in `ExecutePipelineAsync`).
- Polly retry: exponential backoff, max delay 2 minutes, configurable max attempts per handler.
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
| `bool`, `int`, `double`, `string`, `timespan`, `version`, `rate` | Typed sensor values |
| `intBar`, `doubleBar` | Bar sensor values |
| `file` | File sensor values |
| `list` | Batch sensor values |
| `commands` | Server-to-collector commands |
| `testConnection` | Connectivity check |
| `addOrUpdate` | Sensor registration/update |

---

## Known Issues / Limitations

- Polly retry does not configure `ShouldHandle` for HTTP status codes. Only transport-level exceptions trigger retries. HTTP 4xx/5xx responses are accepted as "successful" by Polly and the data is silently lost.
- No circuit breaker pattern. If the server is down for extended periods, each batch attempt will exhaust all retry attempts before failing.
