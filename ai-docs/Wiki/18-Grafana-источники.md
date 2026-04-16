# Поддерживаемые источники данных Grafana

## ИСТОЧНИКИ ДАННЫХ

### JSON DATASOURCE

Плагин: https://grafana.com/grafana/plugins/simpod-json-datasource/

JSON Datasource читает данные в 2 форматах: datapoints и table. В таблице показано, какие форматы Grafana поддерживаются разными типами датчиков.

| Тип датчика HSM | Datapoints | Table |
|-----------------|------------|-------|
| Boolean         | ✅         | ✅    |
| Integer         | ✅         | ✅    |
| Double          | ✅         | ✅    |
| String          | ✅         | ✅    |
| TimeSpan        | ✅         | ✅    |
| IntegerBar      | ❌         | ✅*   |
| DoubleBar       | ❌         | ✅*   |
| File            | ❌         | ❌    |

* ✅ — поддерживается
* ✅* — поддерживается, но формат немного отличается
* ❌ — не поддерживается
