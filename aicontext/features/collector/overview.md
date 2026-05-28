# Collector Features Overview

> Owner: collector | Last reviewed: 2026-05-28 | Canonical: yes

The collector area owns the .NET `HSMDataCollector` library: public collector APIs,
sensor registration, sensor lifecycle, queues, scheduling, transport, logging, and
default sensors.

## Feature Folders To Add Here

- `lifecycle/` - `DataCollector` start/stop/dispose, state transitions, events.
- `scheduler/` - collector scheduler, scheduled tasks, periodic sensor work.
- `sensors/` - sensor base classes, default sensors, options, path behavior.
- `transport/` - sender/client behavior, retry, connection tests, file sending.
- `queues/` - sync queues, buffering, overflow/backpressure behavior.
- `logging/` - collector logging and message deduplication.

Create folders from `../_TEMPLATE_feature.md` as work lands.
