# Manual check: a native WebSocket send to a peer that stops reading (Schannel)

Owner: shared | #1848 step 6 (table sensors) | native collector 0.15.2

## Why it is manual

The native collector's WebSocket is libcurl in connect-only mode. On Windows the vcpkg libcurl uses
Schannel, and Schannel's send waits on the socket itself, with no limit in connect-only mode (libcurl
8.21.0, `lib/vtls/schannel.c` `schannel_send()`, whose timeout comes from `Curl_timeleft_ms()`, which is 0
for a connect-only handle: `lib/connect.c` `timeleft_now_ms()`). Since 0.15.2 the collector's
`WebSocketConnection` waits for the socket before every call into libcurl and hands libcurl at most one TLS
record per call, so that wait is never reached (`aicontext/features/collector/http-client/feature.md`,
"What bounds a send").

The CTest tests are plaintext (`tests/ws_test_server.hpp`). Against a plaintext peer libcurl answers
`CURLE_AGAIN` and the collector's own wait bounded a send even before 0.15.2, so they cannot show the
Schannel case. They pin the property that matters through a trace of every `curl_ws_send` (each one comes
right after a wait that found the socket writable and passes at most 16000 bytes). This check runs the real
case: wss, Schannel, a peer that reads nothing.

## How to run it

1. A self-signed certificate in a folder of your own (any tool; with OpenSSL):
   `openssl req -x509 -newkey rsa:2048 -nodes -keyout key.pem -out cert.pem -days 7 -subj /CN=localhost`
2. The peer, from that folder's parent or anywhere (Python 3, standard library only):
   `python -I src/native/collector/tests/manual/stall_peer.py 18543 <certfolder> 8`
   It takes every upgrade, reads nothing for 8 s, then reads to the end and closes.
3. The test binary of a Windows build with `HSM_COLLECTOR_HTTP=ON` (vcpkg toolchain; Release for the
   numbers below):
   `set HSM_WS_STALL_URL=wss://127.0.0.1:18543/` and optionally `set HSM_WS_STALL_RUNS=10`, then
   `hsm_collector_tests.exe native_ws_live_stalled_send`.

It is not registered in CTest and skips without `HSM_WS_STALL_URL`.

## What it does and what to expect

Each run opens its own connection (the peer serves each on a thread of its own):

- a 16 MiB binary send with a 2 s timeout: `sent=0 after ~2000 ms, error='send timed out', open=0`;
- it counts how many 16000-byte frames a connection takes before one times out (the fill), then, on a new
  connection each time after that many frames:
  - a 4-byte text with a 2 s timeout: `failed: send timed out after ~2000 ms, libcurl writes=0`;
  - `Close(0)`: `returned after 0 ms, libcurl writes=0` (no close frame to a socket that does not take it);
  - `Close` with a 1 s linger: `returned after ~1000 ms, libcurl writes=0`.

A run where the fill left the socket still writable (the OS moved bytes on meanwhile) shows the send or the
close frame going out at once (`sent after 0 ms`, `libcurl writes=1`); that is not a failure. A failure is any
result well past its bound.

## Numbers

Windows 11, libcurl 8.21.0 (vcpkg, Schannel), loopback, the peer above with an 8 s pause, 10 runs each.

Before 0.15.2 (the prototype of #1848 step 6 over the same `WebSocketConnection`):

| What | Result |
|---|---|
| 16 MiB, 2 s timeout | returned when the peer read again: 8.2 s with an 8 s pause, 15.0 s with 15 s, 20 s with 20 s |
| 4-byte text after a send that filled the buffers | 8.0 s (the peer's pause) |
| `Close` with a 1 s linger after a full buffer | 8.0 s |
| `shutdown()` of the socket from another thread at 2 s | did not end the wait (6.0 s more, until the peer read) |

0.15.2 (the final code, `native_ws_live_stalled_send`, 2026-11-26):

| What | Result |
|---|---|
| 16 MiB, 2 s timeout | 2004–2014 ms, "send timed out", 10/10 |
| fill | 12 frames of 16000 bytes |
| 4-byte text, 2 s timeout | 2000–2011 ms "send timed out" with no call into libcurl in 9 runs; 1 run sent at once (the buffer had room) |
| `Close(0)` | 0 ms in 10/10; no close frame written in 9, written at once in 1 |
| `Close` with a 1 s linger | 1003–1011 ms, no close frame written, 10/10 |

## What it rests on, and what it does not cover

- A send of one TLS record on a socket that `select` reported writable was accepted whole by Windows in
  every call measured (about 10,600 calls of the prototype, stalled and healthy peers; every call of the run
  above). Microsoft does not document it.
- Post-handshake TLS messages: a message that makes Schannel's DecryptMessage return `SEC_I_RENEGOTIATE`
  (libcurl says that is "not necessarily literal TLS renegotiation") can still hold a call inside libcurl for up
  to 60 s (`schannel_recv_renegotiate`, `MAX_RENEG_BLOCK_TIME`). The collector's deadline does not end it; a
  cancel does within about a second, through the progress callback `Open` installs (read in libcurl's source,
  not tested). Whether Windows returns that status for TLS 1.3 post-handshake messages was not checked.
- A peer that has gone silent (packets dropped) could not be produced on Windows without a firewall rule.
  The bound acts on the local socket only, so the peer's state does not change it; on Linux (a container
  disconnected from its network) the send ended at its timeout.
