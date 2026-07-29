# HSM DataCollector

## New default sensors (`AddAllComputerSensors`)
* **Top-CPU sensors** — per-process CPU sensors are now part of the default computer-sensor set; each carries the executable path and a bounded list of system processes, all nested under `.computer` (#1179, #1318).
* **Per-interface network speed sensors** — rx/tx MB/s sensors per active network interface, nested under `.computer`. Only interfaces carrying traffic are surfaced (#1189).

## Reliability and queue
* Retry policy no longer drops newer telemetry when a stale retry races queue overflow; FIFO-head ordering preserved across retries (#1088, #1090).
* Collector shutdown is bounded and crash-isolated: hung user tasks, reentrant lifecycle transitions, and dispose-during-start no longer leak queues or crash the host (#1102, #1103, lifecycle state machine refactor).
* Accepted telemetry is preserved across post-stop flush retries; last-value sensors are flushed on stop and dispose.

## Behavior and configuration
* HTTP client timeout is now configurable; untrusted TLS certificates require explicit opt-in (`AllowUntrustedServerCertificate`).
* Request payloads are redacted from send logs.

## Cross-language default-sensor conformance
* Managed and native collectors now share a conformance suite covering default-sensor registration and options parity; this release locks the managed side of that contract.
