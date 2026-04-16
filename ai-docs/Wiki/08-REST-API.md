# REST API

HSM предоставляет HTTP API для отправки данных датчиков и запроса истории. Используйте его на любом языке или в любой среде, где пакет NuGet недоступен.

Базовый URL: `https://your-hsm-server:44330/api/sensors`

Все запросы используют HTTPS. Аутентификация — через заголовок Key.

---

## Аутентификация

Включайте ключ доступа в каждый запрос в заголовке:

`Key: YOUR_ACCESS_KEY`

Ключ доступа создаётся для каждого продукта в веб-интерфейсе: Продукты → Ключи доступа.

Альтернативно ключ можно передать в теле запроса в поле `key` (не рекомендуется — предпочитайте заголовок).

---

## Отправка значений датчиков

Все эндпоинты принимают POST с Content-Type: application/json.

### Bool
`POST /api/sensors/bool`
```json
{
  "path": "MyApp/is_running",
  "value": true,
  "comment": "необязательный комментарий",
  "status": 0
}
```

### Int
`POST /api/sensors/int`
```json
{
  "path": "MyApp/queue_depth",
  "value": 42
}
```

### Double
`POST /api/sensors/double`
```json
{
  "path": "MyApp/cpu_usage",
  "value": 87.3,
  "comment": "после скачка"
}
```

### String
`POST /api/sensors/string`
```json
{
  "path": "MyApp/last_error",
  "value": "Таймаут соединения через 30с"
}
```

### TimeSpan
`POST /api/sensors/timespan`
```json
{
  "path": "MyApp/request_duration",
  "value": "00:00:00.350"
}
```
Формат значения: `hh:mm:ss.fff` (стандартная строка .NET TimeSpan).

### Version
`POST /api/sensors/version`
```json
{
  "path": "MyApp/version",
  "value": "2.1.0"
}
```

### Rate
`POST /api/sensors/rate`
```json
{
  "path": "MyApp/requests_per_sec",
  "value": 150.5
}
```

### IntegerBar
`POST /api/sensors/intBar`
```json
{
  "path": "MyApp/response_time_ms",
  "min": 45,
  "max": 320,
  "mean": 112.5,
  "count": 200,
  "openTime": "2025-03-22T09:00:00Z",
  "closeTime": "2025-03-22T09:05:00Z"
}
```

### DoubleBar
`POST /api/sensors/doubleBar`
```json
{
  "path": "MyApp/cpu_samples",
  "min": 12.1,
  "max": 95.4,
  "mean": 43.7,
  "count": 300,
  "openTime": "2025-03-22T09:00:00Z",
  "closeTime": "2025-03-22T09:05:00Z"
}
```

### File
`POST /api/sensors/file`
```json
{
  "path": "MyApp/daily_report",
  "fileName": "report_2025-03-22",
  "extension": "csv",
  "value": "BASE64_ENCODED_FILE_CONTENT"
}
```
`value` — содержимое файла, закодированное в Base64.

---

## Отправка нескольких значений

`POST /api/sensors/list`

Отправьте до 1000 значений в одном запросе. Каждый объект должен включать дискриминатор типа:
```json
[
  { "type": "bool",   "path": "MyApp/alive",     "value": true },
  { "type": "double", "path": "MyApp/cpu",        "value": 45.2 },
  { "type": "int",    "path": "MyApp/queue",      "value": 17 },
  { "type": "string", "path": "MyApp/status",     "value": "OK" }
]
```
Допустимые значения type: `bool`, `int`, `double`, `string`, `timespan`, `version`, `rate`, `intBar`, `doubleBar`, `file`.

---

## Создание или обновление метаданных датчика

`POST /api/sensors/addOrUpdate`

Создайте датчик заранее или обновите его метаданные (описание, TTL и т.д.) без отправки значения:
```json
{
  "path": "MyApp/cpu_usage",
  "sensorType": "double",
  "description": "Загрузка ЦП основного процесса",
  "unit": "Percent",
  "ttl": "00:05:00",
  "keepHistory": "30.00:00:00"
}
```

---

## Запрос истории

`POST /api/sensors/history`

Получите исторические значения датчика. Ключ доступа должен иметь разрешение на чтение.

По диапазону времени:
```json
{
  "path": "MyApp/cpu_usage",
  "from": "2025-03-22T00:00:00Z",
  "to":   "2025-03-22T23:59:59Z"
}
```

По количеству (последние N значений):
```json
{
  "path": "MyApp/cpu_usage",
  "from": "2025-03-22T00:00:00Z",
  "count": 100
}
```
Возвращает массив JSON с историческими значениями и метками времени.

---

## Экспорт истории в файл

`POST /api/sensors/historyFile`

То же, что и history, но возвращает файл CSV или TXT:
```json
{
  "path": "MyApp/cpu_usage",
  "from": "2025-03-22T00:00:00Z",
  "to":   "2025-03-22T23:59:59Z",
  "fileName": "cpu_export",
  "extension": "csv",
  "isZipArchive": false
}
```
Установите `"isZipArchive": true` для получения файла в сжатом виде `.zip`.

---

## Проверка соединения

`GET /api/sensors/testConnection`

Возвращает 200 OK, если сервер доступен. Аутентификация не требуется.
```bash
curl https://your-hsm-server:44330/api/sensors/testConnection
```

---

## HTTP-коды состояния

| Код | Значение | Описание |
|-----|----------|----------|
| `200` | OK | Значение успешно принято |
| `400` | Bad Request | Неправильно сформированное тело запроса |
| `406` | Not Acceptable | Неверный ключ доступа, неверный продукт или несоответствие типа датчика |

---

## Общие поля запросов

Все эндпоинты для одиночных значений имеют эти необязательные поля:

| Поле | Тип | Описание |
|------|-----|----------|
| `path` | `string` | Путь датчика внутри продукта (например, `"Service/cpu"`) |
| `comment` | `string` | Необязательный текст, прикрепляемый к значению |
| `status` | `int` | `0` = Ok, `1` = Error (переопределяет статус, вычисленный политикой) |
| `time` | `string` | Временная метка UTC в формате ISO 8601 (по умолчанию — время получения сервером) |
| `key` | `string` | Ключ доступа (вместо этого рекомендуется использовать заголовок) |
