# Collector transport chaos tests

Дата прогона: 2026-05-26.

Коротко: добавлен быстрый test suite из 16 транспортных chaos-сценариев и отдельный gated soak-тест. Быстрые тесты ломают соединение ниже уровня `HttpListener`: принимают TCP и закрывают, принимают и молчат, держат открытый порт без application-level accept, медленно читают body, отдают битый HTTP, сбрасывают соединение во время request body, подвешивают только `/commands` или только data endpoint, запускают много collectors и шлют большие payload/file payload. Gated soak повторяет mixed transport suite на одном сервере 30+ секунд и смотрит реальные accepted TCP connections и ресурсный тренд.

Код тестов:

`src/collector/HSMDataCollector.Tests/CollectorTransportChaosTests.cs`

Связанные suites:

- `docs/test/CollectorAdversarialTests.md`
- `docs/test/CollectorResourceLeakTests.md`
- `docs/test/CollectorStressTests.md`

## Общий контроль

Каждый transport-chaos тест проверяет, что:

- `Dispose()` коллектора завершается в ограниченный timeout;
- после `Dispose()` нет зависших TCP `ESTABLISHED` соединений к портам chaos-серверов;
- тестовый сервер реально получил запросы и выполнил нужный chaos-сценарий;
- retry storm не превращается в неограниченную лавину запросов;
- тяжелые payload/file сценарии доходят до реальной отправки и не оставляют открытых соединений.

Важно: отдельные быстрые сценарии - это smoke/regression проверки, а не доказательство отсутствия socket leak. Из-за batching и retry delay один быстрый сценарий может дать всего несколько HTTP request-ов. Для проверки утечки сокетов добавлен gated-тест `Mixed_transport_chaos_suite_repeated_on_one_server_stays_bounded`: он держит один raw TCP server, по очереди переключает chaos-сценарии и повторяет этот mini-suite до истечения заданного времени.

Для ресурсных трендов по памяти, handles, threads, private bytes и working set используется отдельный suite:

`docs/test/CollectorResourceLeakTests.md`

## Команда запуска

Только transport-chaos suite:

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorTransportChaosTests" --logger "console;verbosity=detailed"
```

Обычный полный прогон всех быстрых тестов:

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --logger "console;verbosity=minimal"
```

Single-server transport soak на 30 секунд:

```powershell
$env:HSM_COLLECTOR_RUN_TRANSPORT_SOAK="1" # или HSM_COLLECTOR_RUN_SUITE_SOAK="1"
$env:HSM_COLLECTOR_TRANSPORT_SOAK_SECONDS="30" # или общий HSM_COLLECTOR_SUITE_SOAK_SECONDS="30"
$env:HSM_COLLECTOR_TRANSPORT_SOAK_MAX_SECONDS="120" # или общий HSM_COLLECTOR_SUITE_SOAK_MAX_SECONDS="120"
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~Mixed_transport_chaos_suite_repeated_on_one_server_stays_bounded" --logger "console;verbosity=detailed"
```

Параметры soak-теста:

| Переменная | Default | Что меняет |
| --- | ---: | --- |
| `HSM_COLLECTOR_RUN_TRANSPORT_SOAK` | off | Включает gated soak-тест |
| `HSM_COLLECTOR_RUN_SUITE_SOAK` | off | Включает все gated suite repeat-тесты, включая transport soak |
| `HSM_COLLECTOR_TRANSPORT_SOAK_SECONDS` | 30 | Длительность mixed suite |
| `HSM_COLLECTOR_SUITE_SOAK_SECONDS` | 30 | Общая длительность suite repeat, используется transport soak если transport-specific переменная не задана |
| `HSM_COLLECTOR_TRANSPORT_SOAK_MAX_SECONDS` | 120 | Hard safety limit для transport soak |
| `HSM_COLLECTOR_SUITE_SOAK_MAX_SECONDS` | 120 | Общий hard safety limit для suite repeat |
| `HSM_COLLECTOR_TRANSPORT_SOAK_COLLECTORS` | 8 | Сколько collectors одновременно создается на фазу |
| `HSM_COLLECTOR_TRANSPORT_SOAK_VALUES` | 250 | Сколько `AddValue()` вызывает каждый collector на фазу |
| `HSM_COLLECTOR_TRANSPORT_SOAK_MIN_CONNECTIONS` | 200 | Минимум accepted TCP connections, ниже тест считается слишком слабым |

## Результат локального прогона

Transport-chaos suite:

```text
Total tests: 17
Passed: 16
Skipped: 1
Failed: 0
Total time: ~27 seconds
```

Полный быстрый test run:

```text
Passed: 29
Skipped: 7
Failed: 0
Total: 36
Duration: 33 seconds
```

Skipped тесты - это намеренно длинные stress/soak проверки, которые включаются через env-переменные.

Single-server transport soak, 30 секунд:

```text
Total tests: 1
Passed: 1
Accepted TCP connections: 659
Requests: 659
Dropped: 151
Hung: 128
Slow reads: 128
Headers-only: 96
Malformed HTTP: 60
TCP resets: 96
ESTABLISHED after settle: 0
TIME_WAIT after settle: 440
Post-warm-up trend:
  handles: 1294 -> 1297
  threads: 108 -> 105
```

Последний общий repeat-прогон всех suite на 30 секунд дал для transport:

```text
AddValue calls: 244000
Accepted TCP connections: 1150
Requests: 1148
Command/registration requests: 1125
Data requests: 19
Dropped: 321
Hung: 171
Slow reads: 164
Headers-only: 166
Malformed HTTP: 166
TCP resets: 160
ESTABLISHED after settle: 0
TIME_WAIT after settle: 820
Post-warm-up trend:
  handles: 1097 -> 1107
  threads: 64 -> 64
```

## 16 сценариев по шагам

### 1. Accept and drop

Тест:

`Server_accepts_and_disconnects_repeatedly_does_not_leak_sockets`

Шаги:

1. Поднять raw TCP chaos-server.
2. На каждый request принять соединение.
3. Подождать `100 ms`.
4. Закрыть соединение без HTTP response.
5. Отправить значения из 12 сенсоров.
6. Вызвать `Dispose()`.
7. Проверить `ESTABLISHED=0`.

Локальный счетчик:

```text
requests=4; commands=2; data=2; dropped=4
```

### 2. Accept and never respond

Тест:

`Server_accepts_and_never_responds_dispose_cancels_requests`

Шаги:

1. Сервер принимает TCP.
2. Читает HTTP headers.
3. Не отправляет response.
4. Коллектор работает с `RequestTimeout=600 ms`.
5. Вызвать `Dispose()`.
6. Проверить, что зависшие запросы отменены и `ESTABLISHED=0`.

Локальный счетчик:

```text
requests=3; commands=1; data=2; hung=3
```

### 3. Open socket, no accept

Тест:

`Server_socket_is_open_but_never_accepts_while_values_are_added_does_not_hang_or_leak`

Шаги:

1. Поднять `TcpListener` на `127.0.0.1` с backlog `1`.
2. Не вызывать `AcceptTcpClientAsync()` вообще.
3. Создать collector с коротким `RequestTimeout=300 ms`.
4. Создать 16 double-сенсоров.
5. Параллельно вызвать `AddValue()` 16000 раз.
6. Подождать 2 секунды, чтобы sender попытался отправлять в открытый, но не принимающий сервер.
7. Вызвать `Dispose()`.
8. Остановить listener.
9. Проверить, что не осталось TCP `ESTABLISHED`, а handles/threads/memory bounded.

Локальный счетчик:

```text
addValues=16000
handles=599->859
threads=23->44
managedGc=2125760->5506456
private=42909696->56283136
workingSet=68313088->82972672
tcpEstablished=0
tcpTimeWait=0
```

### 4. Slow request-body read

Тест:

`Server_reads_request_body_slowly_does_not_block_dispose`

Шаги:

1. Сервер принимает request.
2. Читает body по 1 байту.
3. Между байтами ждет `5 ms`.
4. Потом отвечает `200 OK`.
5. Коллектор должен завершить `Dispose()` без зависания.

Локальный счетчик:

```text
requests=4; commands=2; data=2; slowReads=4; bytes=107
```

### 5. Headers sent, body never completes

Тест:

`Server_sends_headers_and_never_completes_body_does_not_hang_dispose`

Шаги:

1. Сервер принимает request.
2. Отправляет `HTTP/1.1 200 OK`.
3. Указывает большой `Content-Length`.
4. Body не отправляет.
5. Коллектор должен отменить чтение по timeout/dispose.

Локальный счетчик:

```text
requests=4; commands=2; data=2; headerOnly=4; bytes=38740
```

### 6. Malformed HTTP

Тест:

`Server_returns_malformed_http_does_not_leak_connections`

Шаги:

1. Сервер принимает TCP.
2. Отправляет текст, который не является HTTP response.
3. Закрывает соединение.
4. Коллектор должен пережить protocol error.
5. После `Dispose()` не должно остаться `ESTABLISHED`.

Локальный счетчик:

```text
requests=4; commands=2; data=2; malformed=4
```

### 7. Reset during request body

Тест:

`Server_resets_connection_during_request_body_does_not_leak_connections`

Шаги:

1. Сервер принимает request.
2. Начинает читать body.
3. После первых `128` байт делает TCP reset через `LingerOption(true, 0)`.
4. Коллектор должен корректно освободить соединение.

Локальный счетчик:

```text
requests=2; commands=1; data=1; resets=2; bytes=256
```

### 8. Command endpoint hangs, data endpoint works

Тест:

`Command_endpoint_hangs_data_endpoint_still_disposes`

Шаги:

1. `/commands` принимает request и не отвечает.
2. Data endpoint отвечает `200 OK`.
3. Коллектор отправляет значения.
4. Проверить, что data request-ы продолжаются.
5. `Dispose()` должен завершиться.

Локальный счетчик:

```text
requests=8; commands=1; data=7; ok=7; hung=1; bytes=110007
```

### 9. Data endpoint hangs, command endpoint works

Тест:

`Data_endpoint_hangs_command_endpoint_still_disposes`

Шаги:

1. `/commands` отвечает `200 OK`.
2. Data endpoint принимает request и не отвечает.
3. Коллектор должен отменить data request по timeout/dispose.
4. `Dispose()` должен завершиться.

Локальный счетчик:

```text
requests=3; commands=1; data=2; ok=1; hung=2; bytes=3497
```

### 10. Server starts after connection refused

Тест:

`Server_starts_after_connection_refused_collector_recovers`

Шаги:

1. Выбрать свободный порт.
2. Запустить collector до запуска сервера.
3. Отправить значения, пока порт закрыт.
4. Через `800 ms` поднять сервер на этом же порту.
5. Отправить еще значения.
6. Проверить, что collector восстановился и сервер получил request-ы.

### 11. Many collectors to one flaky server

Тест:

`Many_collectors_to_one_flaky_server_do_not_exhaust_resources`

Шаги:

1. Поднять один chaos-server.
2. Запустить 8 collectors на один порт.
3. Каждый collector отправляет 200 значений.
4. Каждый 3-й request сервер закрывает без response.
5. Все collectors dispose-ятся.
6. Проверить `ESTABLISHED=0`.

### 12. Many collectors on many flaky ports

Тест:

`Many_collectors_on_many_flaky_ports_do_not_leave_connections`

Шаги:

1. Поднять 5 chaos-серверов на разных портах.
2. Запустить 5 collectors.
3. Каждый collector отправляет 200 значений.
4. Каждый 2-й request получает TCP reset.
5. Все collectors и servers dispose-ятся.
6. Проверить `ESTABLISHED=0` по всем портам.

Локальный счетчик:

```text
5 servers x requests=4; resets=2 per server
```

### 13. Huge string and comment payload

Тест:

`Huge_string_and_comment_payload_under_disconnects_stays_bounded`

Шаги:

1. Создать string sensor.
2. Сформировать payload `64 KB`.
3. Сформировать comment `64 KB`.
4. Отправить 20 значений.
5. Каждый 2-й request сервер закрывает без response.
6. Проверить, что реально ушло больше `512 KB` request body и `ESTABLISHED=0`.

Локальный счетчик:

```text
requests=3; data=2; dropped=1; bytes=1334938
```

### 14. File sensor flood

Тест:

`File_sensor_flood_under_disconnects_releases_files_and_sockets`

Шаги:

1. Создать 5 временных файлов по `64 KB`.
2. Создать file sensor.
3. Отправить все файлы.
4. Каждый 2-й request сервер закрывает без response.
5. Удалить временные файлы.
6. Проверить `ESTABLISHED=0`.

Локальный счетчик:

```text
requests=6; data=5; dropped=3; bytes=467736
```

### 15. Dispose while HTTP request is mid-flight

Тест:

`Dispose_while_http_request_is_mid_flight_closes_connection`

Шаги:

1. Data endpoint принимает request.
2. Тест ждет факта принятия data request.
3. Пока сервер молчит, тест вызывает `Dispose()`.
4. Проверить, что `Dispose()` завершился и `ESTABLISHED=0`.

### 16. Constant disconnect retry storm

Тест:

`Constant_disconnect_retry_storm_stays_bounded`

Шаги:

1. Сервер принимает и сразу закрывает каждый request.
2. Коллектор отправляет 12 сенсоров x 200 значений.
3. Тест ждет `3` секунды.
4. Проверить, что request count не ушел в лавину.
5. Проверить, что CPU test process не сгорел на retry loop.

Локальный счетчик:

```text
requests=8; dropped=8; retryStormCpuMs=31.25
```

## Золотая середина по времени

Новый transport suite специально сделан коротким:

- 16 сценариев;
- raw TCP chaos вместо долгого внешнего сервера;
- короткие `RequestTimeout` в пределах `500-800 ms`;
- весь suite проходит примерно за `25` секунд;
- полный обычный test run с уже существующими тестами проходит примерно за `28` секунд.

Gated soak-проверка сделана отдельно:

- один raw TCP server и один порт;
- сценарии идут последовательно: accept/drop, never respond, slow body read, headers-only, malformed HTTP, TCP reset;
- mini-suite повторяется по кругу `30` секунд по умолчанию;
- проверяется интегральный результат: нет ли socket/resource leak после смеси проблем;
- если этот тест падает, дальше можно временно убрать сценарии из списка и найти виновника бинарным поиском.
