# HSM DataCollector 3.10.0-preview

Prerelease for testing. Changes since 3.5.0 (3.6-3.9 were not published).

## New
* **Collector session** (opt-in, `CollectorOptions.Transport = Session`): one WebSocket to the server on 443 instead of the HTTP routes, no fallback to HTTP. Carries values, registrations, files (up to the server's frame limit, about 1 MiB by default) and server directives.
* **TCP connection failure rate** sensor (`AddNetworkConnectionFailuresRate`, Windows), same source as the native collector.
* **Server directive channel**: extra request headers and `X-Hsm-Directive` answers, as in the native collector.
* **`SensorOptions.PreviousPaths`**: history follows a sensor whose path changed (needs a server with sensor move).

## Behaviour changes
* `Service alive` now registers its alert with a 5-minute inactivity period. Before, it carried none and never fired on default settings: expect real alerts for collectors that are off.
* A registration the server rejects with a 4xx is reported once and not sent again; a command send is a single attempt, retried on the next cycle.
* Commands reach the server in the order they were issued, also after failed sends.
* Stop drains registrations first, under one budget; the first failed send ends the drain. The package in flight is kept on Dispose.

## Reliability
* Send failures, queue overflow and top-CPU sampling faults are reported (deduplicated) instead of hidden.
* Lifecycle races fixed: the obsolete `Initialize`, a failed start during Stop/Dispose.
