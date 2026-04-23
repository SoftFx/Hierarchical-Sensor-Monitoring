# Datasources

## Json Datasource
Plugin: https://grafana.com/grafana/plugins/simpod-json-datasource/

Json Datasource readsdata in 2 formats: **datapoints** and **table**.
The table shows which Graphana formats are supported by different sensor types.

| HSM sensor type | Datapoints | Table  |
| :---            |    :---:   | :---:  |
| Boolean         | ✅        |   ✅   |
| Integer         | ✅        |   ✅   |
| Double          | ✅        |   ✅   |
| String          | ✅        |   ✅   |
| TimeSpan        | ✅        |   ✅   |
| IntegerBar      | ❌        |   ✅*  |
| DoubleBar       | ❌        |   ✅*  |
| File            | ❌        |   ❌   |

* ✅ - supported (✅* supported but the format is a bit different)
* ❌ - unsupported