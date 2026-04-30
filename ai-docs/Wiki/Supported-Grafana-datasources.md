# Supported Grafana Datasources

This page shows which HSM sensor types are supported in Grafana and in what format.

See also: [Integration with Grafana](Integration-with-Grafana), [Sensor Types](Sensor-types)

---

## JSON Datasource

Plugin: https://grafana.com/grafana/plugins/simpod-json-datasource/

JSON Datasource reads data in 2 formats: **datapoints** and **table**.

The table below shows which Grafana formats are supported by different HSM sensor types.

| HSM Sensor Type | Datapoints | Table |
|---|---|---|
| **Bool** | ✅ | ✅ |
| **Int** | ✅ | ✅ |
| **Double** | ✅ | ✅ |
| **String** | ✅ | ✅ |
| **TimeSpan** | ✅ | ✅ |
| **IntegerBar** | ❌ | ✅* |
| **DoubleBar** | ❌ | ✅* |
| **File** | ❌ | ❌ |

**Legend:**
- ✅ — Supported
- ✅* — Supported, but the format is slightly different (bar sensors return aggregated data)
- ❌ — Unsupported

---

## See Also

- [Integration with Grafana](Integration-with-Grafana) — Step-by-step Grafana setup guide
- [Sensor Types](Sensor-types) — All available HSM sensor types
- [HSM DataCollector → Sensor Options](HSMDataCollector#sensor-options) — Enable Grafana for individual sensors