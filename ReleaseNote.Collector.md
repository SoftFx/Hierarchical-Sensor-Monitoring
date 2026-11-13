# HSM DataCollector 4.0.0-preview

Prerelease for testing. Changes since 3.5.0 (3.6-3.9 were not published).

## Breaking
* **The collector session is the default transport** (ADR-0019): one WebSocket to the server on 443, no fallback to HTTP. Needs **HSMServer 3.43.0** or later and a proxy that passes WebSockets; otherwise set `CollectorOptions.Transport = Http`. Where no session can exist (.NET Framework with `AllowUntrustedServerCertificate`, an OS without WebSockets) the default stays HTTP and Start says why.

## New
* Collector session: values, registrations, files (up to about 1 MiB by default) and server directives on one connection.
* **TCP connection failure rate** sensor (`AddNetworkConnectionFailuresRate`, Windows).
* Server directive channel: extra request headers and `X-Hsm-Directive` answers.
* **`SensorOptions.PreviousPaths`**: history follows a sensor whose path changed.

## Behaviour changes
* `Service alive` registers its alert with a 5-minute inactivity period; before, it never fired on default settings.
* A registration rejected with a 4xx is reported once and not resent; a command send is one attempt per cycle, in the order issued.
* Stop drains registrations first, under one budget; the package in flight is kept on Dispose.

## Reliability
* Send failures, queue overflow and top-CPU sampling faults are reported (deduplicated).
* Lifecycle races fixed: the obsolete `Initialize`, a failed start during Stop/Dispose.
