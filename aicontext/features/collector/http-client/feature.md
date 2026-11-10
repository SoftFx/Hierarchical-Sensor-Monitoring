# Feature: HTTP Client

> Owner: collector | Last reviewed: 2026-11-10 | Canonical: yes
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
- **Connection reuse (both collectors).** Sends ride a kept connection, not a new TCP+TLS handshake per request. Managed: one pooled `HttpClient` connection, retired after 5 minutes (`PooledConnectionLifetime`; `ServicePoint.ConnectionLeaseTimeout` on net472) so DNS is resolved again (#1102-E4). Native: `HttpTransport` keeps one libcurl easy handle and resets it between requests (a concurrent caller gets its own handle, closed afterwards); the kept handle is retired 5 minutes after its creation, a connection idle for more than 60 s is not reused (`CURLOPT_MAXAGE_CONN`, the managed handler's idle timeout), TCP keep-alive is on, and a handle whose transfer failed is destroyed, never kept (#1653). Delivery stays at-least-once in both collectors: a batch the server stored but whose response was lost is sent again. There is still no application-level session: the server cannot push to a collector and learns that one went away only from TTL.
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
| `../../src/native/collector/src/hsm_ws_codec.hpp` | Native RFC 6455 pieces: handshake digest, client frame encoding, server frame parsing (pure, no libcurl) |
| `../../src/native/collector/src/hsm_ws_connection.{hpp,cpp}` | Native WebSocket client connection over a libcurl `CONNECT_ONLY` handle (#1662); not wired into the send path yet |

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

- `hsm_http_transport.{hpp,cpp}` — a libcurl easy-handle wrapper (`Post`/`Get`) that keeps its handle between requests (#1653: before 0.11.3 every request created and destroyed its own handle, i.e. its own connection), confined to one TU so the rest of the core never sees `<curl/curl.h>` and the `-Werror`/`/WX` gate stays green. `CURLOPT_TIMEOUT_MS` ↔ `RequestTimeout`, `CURLOPT_SSL_VERIFYPEER` ↔ `AllowUntrustedServerCertificate`, `CURLOPT_XFERINFOFUNCTION`-abort ↔ `CancelPendingRequests`. Built only when `HSM_COLLECTOR_HTTP=ON` (curl via vcpkg manifest); the default build is curl-free.
- `hsm_http_endpoints.hpp` — pure route table + scheme defaulting (bare host → https; explicit `http` kept only with `AllowPlaintextTransport`), and per-kind / batch / command routing identical to `DataHandlers`/`CommandHandler.GetUri`.
- `hsm_http_retry.hpp` — the retry policy with the same `ShouldRetry` semantics (bounded 5xx retry, unbounded commands exceptions-only, 4xx never).

Routing + retry are pure headers compiled into the always-on core, so they are unit-tested in the default `/WX` lane (`native_http_endpoint_routing_matches_net`, `native_http_retry_policy_matches_net`); the transport is exercised by an in-proc capture-server E2E (`native_http_transport_posts_to_capture_server`) in the `HSM_COLLECTOR_HTTP` CI lane. The .NET side of the 5xx fix is pinned by `Retry5xxParityTests` against the fake server.

### Live send-path wiring (#1097)

`TrySendBatch` delegates to a **sender seam** (`sender_`, a `std::function<bool(std::vector<std::string>&)>`). The default records each batch into `sent_values_` so the behavior corpus and the `sent_count`/`get_sent_json` accessors keep working in both build lanes. In the `HSM_COLLECTOR_HTTP` build, `TestInstallHttpSender()` swaps in a real libcurl POST: the batch is joined into a JSON array and sent to the `/list` route with `Key`/`ClientName` headers, exercising the full `add → queue → worker → POST` pipeline. Installed before `Start` (same one-way contract as the clock seam — the worker only reads `sender_` after `Start`, so the swap needs no lock). `StopWorker` calls `HttpTransport::Cancel()` (aborting an in-flight POST so the join stays bounded) and `StartWorker` calls `ResetCancel()` to re-arm after a restart.

Send semantics: **one POST attempt per batch**. A non-2xx or transport failure returns `false`, so the dispatcher re-enqueues the batch at the tail — the same durable-retry contract the C# queue uses (re-enqueue on `PackageSendingInfo.Error != null`, including 5xx and 4xx). The Polly-equivalent in-send exponential/linear backoff (`hsm_http_retry.hpp`, unit-tested separately) is a non-blocking refinement layered on later; doing it inline would block the single worker thread for up to the policy max-delay per batch. The end-to-end live path is pinned by `native_http_live_send_posts_to_capture_server` (drives a real int value through the collector and asserts the captured POST route/headers/body).

**Public live transport (#1165):** `hsm_collector_use_http_transport` (C ABI) / `Collector::UseHttpTransport()` (C++) makes the libcurl transport reachable from application code. Installed before Start, it (a) POSTs the wire `AddOrUpdate` registration batch to `/commands` at Start (the `Type:0` Command discriminator), and (b) switches values/bars to the **server wire format** (`Time` ISO-8601, bar `OpenTime`/`CloseTime`) via `OutgoingValueJson`/`OutgoingBarJson` — the conformance corpus keeps the canonical/in-memory recorder (`send_wire_` is false there). Pinned by `native_http_registers_sensors_on_start` + the wire assertions in `native_http_live_send_posts_to_capture_server`, and proven end-to-end against a Dockerized HSM server by `examples/server-monitor` (registration + live int/double values readable via `/api/sensors/history`). Before #1165 the transport was test-only and posted canonical JSON, so nothing reached a real server. Still open: per-command error-dictionary parse, in-send Polly backoff, and registering sensors created *while running* (today only the Start-batch registers over HTTP).

### WebSocket client connection (#1662, collector session step 1)

A building block for the collector session (ADR-0017, `docs/initiatives/collector-session-websocket.md`), **not used by the send path yet**.

- `WebSocketConnection` (`hsm_ws_connection.{hpp,cpp}`): `Open(url, headers, subprotocol, timeout)`, `SendText`, `Receive`, `Close`, `Cancel`. One owner thread; `Cancel()` is the only cross-thread member and unblocks a waiting `Open`/`SendText`/`Receive`.
- **libcurl connects, the collector frames.** A `CONNECT_ONLY` easy handle gives TCP, TLS (same `verify_peer` rule as the HTTP transport) and proxy tunnelling; the upgrade request, the `Sec-WebSocket-Accept` check, masking and frame parsing are done with `hsm_ws_codec.hpp` over `curl_easy_send` / `curl_easy_recv`. libcurl's own `curl_ws_*` API is deliberately not used: it needs libcurl 8.11+ built with the feature, which Debian 12 and Ubuntu 22.04/24.04 do not ship, and the Linux probe links the distribution's libcurl.
- `Open` reports the HTTP status of a refused upgrade (404, 503, ...) and the subprotocol the server selected; an offered subprotocol the server did not select fails the open. Request headers with a line break are refused (header injection).
- The upgrade is pinned to HTTP/1.1 (`CURLOPT_HTTP_VERSION`), so a libcurl built with HTTP/2 does not negotiate `h2` in ALPN for a request that is written by hand as HTTP/1.1.
- Every terminal outcome (failed `Open`, failed `SendText`, `Receive` returning `Closed` or `Error`) releases the connection; `IsOpen()` is the truth and nothing is written to a finished WebSocket. No member throws.
- Waiting is a poll in 100 ms slices (`poll` on POSIX, so descriptors above `FD_SETSIZE` are fine). An idle `Receive` therefore wakes ten times a second; the session transport (step 4) needs its own wake-up primitive for outgoing data anyway and replaces this.
- `Receive` returns `Text`, `Timeout` (connection still usable, nothing lost), `Closed` (close frame or TCP close) or `Error`. It assembles fragmented messages and partial reads, answers pings, ignores pongs, refuses binary messages, masked server frames, reserved bits and anything over 8 MiB.
- Tests: `native_ws_codec_handshake_digest_matches_rfc` and `native_ws_codec_frames_match_rfc` (default lane, published vectors); `native_ws_*` in the HTTP lane against `tests/ws_test_server.hpp`, an in-process RFC 6455 server whose framing is written separately from the client's (the handshake digest is shared and pinned by the published vectors). The fixture is plaintext; `native_ws_live_probe` is a manual, env-gated check of the TLS path against a real endpoint.

## Known Issues / Limitations

- No circuit breaker pattern. If the server is down for extended periods, each batch attempt will exhaust all retry attempts before failing.
