# API Features Overview

> Owner: api | Last reviewed: 2026-05-28 | Canonical: yes

The API area owns public contracts shared by HSM Server, DataCollector,
`HSMSensorDataObjects`, Swagger comments, external clients, and wrappers.

## Contract Areas

| Area | Code | Notes |
|---|---|---|
| Sensor DTOs | `src/api/HSMSensorDataObjects` | Serialization compatibility matters. |
| Server controllers | `src/server/HSMServer/Controllers` | Keep request/response docs aligned. |
| Collector public API | `src/collector/HSMDataCollector/PublicInterface` and options | Breaking changes affect integrators. |
| C++ wrapper headers | `src/wrapper/include` | Keep parity with collector public API. |

## Feature Folders To Add Here

- `sensor-requests/` - sensor registration/value request contracts.
- `history-requests/` - history query contracts.
- `collector-public-api/` - collector interfaces/options contracts.
- `wrapper-api/` - C++ wrapper compatibility contract.

Create folders from `../_TEMPLATE_feature.md` as work lands.
