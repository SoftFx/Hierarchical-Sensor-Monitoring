# Feature: HTTP Client

> Owner: collector | Last reviewed: 2026-06-16 | Canonical: yes
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
- `ShouldHandle` (`BaseHandlers.ShouldRetry`, #1096): transport-level exceptions are always retried; a returned **5xx** is retried too, but **only on the bounded** data/priority/file pipelines — the unbounded command pipeline stays exceptions-only so a persistent 5xx cannot hang it forever. **4xx** are permanent and never retried. A 5xx that exhausts the bounded budget still falls through to queue re-enqueue, so no data is lost.
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
| `Client/HttpsClient/RequestHandlers/BaseHandlers.cs` | Polly pipeline, send logic, error logging, `ShouldRetry` (4xx/5xx, #1096) |
| `../../src/native/collector/src/hsm_http_transport.{hpp,cpp}` | Native (C++) libcurl transport — Post/Get, timeout, cert-bypass, xfer-abort cancel |
| `../../src/native/collector/src/hsm_http_endpoints.hpp` | Native route table + scheme defaulting (mirror of `Endpoints.cs` + `*Handlers.GetUri`) |
| `../../src/native/collector/src/hsm_http_retry.hpp` | Native retry policy (mirror of the Polly pipelines + `ShouldRetry`) |

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

## Native port (C++) — #1096

The native collector (`src/native/collector`) emits the **byte-identical** wire stream (see `../../api/wire-contract/feature.md`) and ships a real HTTP transport that mirrors this client:

- `hsm_http_transport.{hpp,cpp}` — a libcurl easy-handle wrapper (`Post`/`Get`), confined to one TU so the rest of the core never sees `<curl/curl.h>` and the `-Werror`/`/WX` gate stays green. `CURLOPT_TIMEOUT_MS` ↔ `RequestTimeout`, `CURLOPT_SSL_VERIFYPEER` ↔ `AllowUntrustedServerCertificate`, `CURLOPT_XFERINFOFUNCTION`-abort ↔ `CancelPendingRequests`. Built only when `HSM_COLLECTOR_HTTP=ON` (curl via vcpkg manifest); the default build is curl-free.
- `hsm_http_endpoints.hpp` — pure route table + scheme defaulting (bare host → https; explicit `http` kept only with `AllowPlaintextTransport`), and per-kind / batch / command routing identical to `DataHandlers`/`CommandHandler.GetUri`.
- `hsm_http_retry.hpp` — the retry policy with the same `ShouldRetry` semantics (bounded 5xx retry, unbounded commands exceptions-only, 4xx never).

Routing + retry are pure headers compiled into the always-on core, so they are unit-tested in the default `/WX` lane (`native_http_endpoint_routing_matches_net`, `native_http_retry_policy_matches_net`); the transport is exercised by an in-proc capture-server E2E (`native_http_transport_posts_to_capture_server`) in the `HSM_COLLECTOR_HTTP` CI lane. The .NET side of the 5xx fix is pinned by `Retry5xxParityTests` against the fake server.

**Remaining integration step:** the native live send path still records to an in-memory sender behind the test seam (`TrySendBatch`); swapping it to dispatch wire bytes through `HttpTransport` in production requires a staged/real HSM server for true request-parity E2E and is the final wiring task of the native port.

## Known Issues / Limitations

- No circuit breaker pattern. If the server is down for extended periods, each batch attempt will exhaust all retry attempts before failing.
