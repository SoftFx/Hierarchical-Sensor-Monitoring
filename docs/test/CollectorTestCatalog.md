# Collector test coverage catalog

Дата: 2026-05-26.

Краткая матрица покрытия тестами `HSMDataCollector`. Цель документа - быстро увидеть, какие группы поведения уже закрыты, сколько тестов есть на тему, где посмотреть код и где прочитать сценарий выполнения.

## Summary

| Группа | Быстрых тестов | Длинных/gated тестов | Код | Подробное описание |
| --- | ---: | ---: | --- | --- |
| Transport chaos | 15 | 0 | `src/collector/HSMDataCollector.Tests/CollectorTransportChaosTests.cs` | `docs/test/CollectorTransportChaosTests.md` |
| Resource leaks | 1 | 1 | `src/collector/HSMDataCollector.Tests/CollectorResourceLeakTests.cs` | `docs/test/CollectorResourceLeakTests.md` |
| Adversarial lifecycle | 10 | 0 | `src/collector/HSMDataCollector.Tests/CollectorAdversarialTests.cs` | `docs/test/CollectorAdversarialTests.md` |
| Flaky server stress | 1 | 1 | `src/collector/HSMDataCollector.Tests/CollectorStressTests.cs` | `docs/test/CollectorStressTests.md` |
| Default sensor smoke | 2 | 0 | `src/collector/HSMDataCollector.Tests/DefaultSensorsTests.cs` | Нет полноценного описания; тесты сейчас фактически пустые |

Текущий быстрый прогон:

```text
Passed: 29
Skipped: 2
Failed: 0
Total: 31
Duration: ~28 seconds
```

## Transport Chaos

Назначение: проверить HTTP/TCP транспорт, отмену зависших запросов, socket cleanup, retry behavior и устойчивость к некорректному серверу.

| Что покрывает | Тестов | Тесты | Где код | Где описание сценария |
| --- | ---: | --- | --- | --- |
| Сервер принимает соединение и сразу закрывает | 1 | `Server_accepts_and_disconnects_repeatedly_does_not_leak_sockets` | `CollectorTransportChaosTests.cs:32` | `CollectorTransportChaosTests.md`, раздел `1. Accept and drop` |
| Сервер принимает соединение и никогда не отвечает | 1 | `Server_accepts_and_never_responds_dispose_cancels_requests` | `CollectorTransportChaosTests.cs:44` | `CollectorTransportChaosTests.md`, раздел `2. Accept and never respond` |
| Сервер очень медленно читает request body | 1 | `Server_reads_request_body_slowly_does_not_block_dispose` | `CollectorTransportChaosTests.cs:57` | `CollectorTransportChaosTests.md`, раздел `3. Slow request-body read` |
| Сервер отправляет headers, но body не заканчивает | 1 | `Server_sends_headers_and_never_completes_body_does_not_hang_dispose` | `CollectorTransportChaosTests.cs:69` | `CollectorTransportChaosTests.md`, раздел `4. Headers sent, body never completes` |
| Сервер возвращает malformed HTTP | 1 | `Server_returns_malformed_http_does_not_leak_connections` | `CollectorTransportChaosTests.cs:82` | `CollectorTransportChaosTests.md`, раздел `5. Malformed HTTP` |
| TCP reset во время request body | 1 | `Server_resets_connection_during_request_body_does_not_leak_connections` | `CollectorTransportChaosTests.cs:94` | `CollectorTransportChaosTests.md`, раздел `6. Reset during request body` |
| `/commands` зависает, data endpoint работает | 1 | `Command_endpoint_hangs_data_endpoint_still_disposes` | `CollectorTransportChaosTests.cs:106` | `CollectorTransportChaosTests.md`, раздел `7. Command endpoint hangs, data endpoint works` |
| Data endpoint зависает, `/commands` работает | 1 | `Data_endpoint_hangs_command_endpoint_still_disposes` | `CollectorTransportChaosTests.cs:121` | `CollectorTransportChaosTests.md`, раздел `8. Data endpoint hangs, command endpoint works` |
| Сервер сначала недоступен, потом появляется | 1 | `Server_starts_after_connection_refused_collector_recovers` | `CollectorTransportChaosTests.cs:136` | `CollectorTransportChaosTests.md`, раздел `9. Server starts after connection refused` |
| Много collectors на один flaky server | 1 | `Many_collectors_to_one_flaky_server_do_not_exhaust_resources` | `CollectorTransportChaosTests.cs:176` | `CollectorTransportChaosTests.md`, раздел `10. Many collectors to one flaky server` |
| Много collectors на много flaky ports | 1 | `Many_collectors_on_many_flaky_ports_do_not_leave_connections` | `CollectorTransportChaosTests.cs:212` | `CollectorTransportChaosTests.md`, раздел `11. Many collectors on many flaky ports` |
| Большой string/comment payload под disconnects | 1 | `Huge_string_and_comment_payload_under_disconnects_stays_bounded` | `CollectorTransportChaosTests.cs:254` | `CollectorTransportChaosTests.md`, раздел `12. Huge string and comment payload` |
| File sensor flood под disconnects | 1 | `File_sensor_flood_under_disconnects_releases_files_and_sockets` | `CollectorTransportChaosTests.cs:276` | `CollectorTransportChaosTests.md`, раздел `13. File sensor flood` |
| `Dispose()` во время in-flight HTTP request | 1 | `Dispose_while_http_request_is_mid_flight_closes_connection` | `CollectorTransportChaosTests.cs:308` | `CollectorTransportChaosTests.md`, раздел `14. Dispose while HTTP request is mid-flight` |
| Retry storm при постоянном disconnect | 1 | `Constant_disconnect_retry_storm_stays_bounded` | `CollectorTransportChaosTests.cs:336` | `CollectorTransportChaosTests.md`, раздел `15. Constant disconnect retry storm` |

## Resource Leaks

Назначение: контролировать процессные ресурсы и TCP states при циклах collector/server/load/dispose.

| Что покрывает | Тестов | Тесты | Где код | Где описание сценария |
| --- | ---: | --- | --- | --- |
| Handles, threads, managed memory after GC, private bytes, working set, TCP `ESTABLISHED`, TCP `TIME_WAIT` на коротком flaky HTTP цикле | 1 | `Collector_releases_resources_after_flaky_http_cycles` | `CollectorResourceLeakTests.cs:31` | `CollectorResourceLeakTests.md`, раздел `Быстрый ресурсный тест` |
| Те же метрики на длинном/gated прогоне с большим количеством циклов | 1 gated | `Collector_releases_resources_after_long_flaky_http_cycles` | `CollectorResourceLeakTests.cs:48` | `CollectorResourceLeakTests.md`, раздел `Длинный ресурсный тест` |

Контролируемые метрики:

| Метрика | Где проверяется |
| --- | --- |
| `Process.HandleCount` | `CollectorResourceLeakTests.cs` |
| TCP connections к тестовым портам | `CollectorResourceLeakTests.cs`, `CollectorTransportChaosTests.cs` |
| TCP `ESTABLISHED` | `CollectorResourceLeakTests.cs`, `CollectorTransportChaosTests.cs` |
| TCP `TIME_WAIT` | `CollectorResourceLeakTests.cs` |
| Managed memory after full GC | `CollectorResourceLeakTests.cs` |
| Private bytes / working set | `CollectorResourceLeakTests.cs` |
| Thread count | `CollectorResourceLeakTests.cs` |

## Adversarial Lifecycle

Назначение: точечно ломать lifecycle, очереди, sensor API и поведение sender-а.

| Что покрывает | Тестов | Тесты | Где код | Где описание сценария |
| --- | ---: | --- | --- | --- |
| CPU spin в `RateSensor` после `double.NaN` | 1 | `Rate_sensor_nan_value_does_not_spin_forever` | `CollectorAdversarialTests.cs:18` | `CollectorAdversarialTests.md`, таблица `Какие сценарии проверяются` |
| `Stop()` после legacy `Initialize(false)` | 1 | `Stop_after_initialize_stops_data_delivery` | `CollectorAdversarialTests.cs:34` | `CollectorAdversarialTests.md`, таблица `Какие сценарии проверяются` |
| Race condition: `Stop()` во время pending `Start(...)` | 1 | `Stop_while_start_is_pending_does_not_leave_collector_running` | `CollectorAdversarialTests.cs:59` | `CollectorAdversarialTests.md`, таблица `Какие сценарии проверяются` |
| `Dispose()` отменяет заблокированный data sender | 1 | `Dispose_cancels_blocked_data_sender` | `CollectorAdversarialTests.cs:78` | `CollectorAdversarialTests.md`, таблица `Какие сценарии проверяются` |
| Постоянные ошибки data sender | 1 | `Permanent_data_sender_failures_do_not_block_dispose` | `CollectorAdversarialTests.cs:99` | `CollectorAdversarialTests.md`, таблица `Какие сценарии проверяются` |
| Постоянные ошибки command sender | 1 | `Permanent_command_sender_failures_do_not_block_dispose` | `CollectorAdversarialTests.cs:122` | `CollectorAdversarialTests.md`, таблица `Какие сценарии проверяются` |
| Создание сенсоров после `Initialize()` при command failures | 1 | `Creating_sensors_after_initialize_under_command_failures_does_not_hang` | `CollectorAdversarialTests.cs:143` | `CollectorAdversarialTests.md`, таблица `Какие сценарии проверяются` |
| Параллельный `AddValue()` во время `Dispose()` | 1 | `Concurrent_add_value_during_dispose_does_not_throw_to_callers` | `CollectorAdversarialTests.cs:164` | `CollectorAdversarialTests.md`, таблица `Какие сценарии проверяются` |
| Overflow маленькой очереди | 1 | `Queue_overflow_under_flood_keeps_collector_responsive` | `CollectorAdversarialTests.cs:201` | `CollectorAdversarialTests.md`, таблица `Какие сценарии проверяются` |
| Повторные `Start()` / `Stop()` циклы | 1 | `Repeated_start_stop_cycles_do_not_leave_sender_active` | `CollectorAdversarialTests.cs:222` | `CollectorAdversarialTests.md`, таблица `Какие сценарии проверяются` |

## Flaky Server Stress

Назначение: нагрузочная отправка sensor values через HSM-like HTTP server с `200`, `500`, broken connections и slow responses.

| Что покрывает | Тестов | Тесты | Где код | Где описание сценария |
| --- | ---: | --- | --- | --- |
| Быстрый flaky HTTP stress под параллельной нагрузкой | 1 | `Collector_survives_transient_server_failures_under_parallel_load` | `CollectorStressTests.cs:30` | `CollectorStressTests.md`, раздел `Быстрый стресс-тест` |
| 10-minute sustained flaky HTTP stress | 1 gated | `Collector_runs_for_ten_minutes_against_flaky_server_under_sustained_load` | `CollectorStressTests.cs:68` | `CollectorStressTests.md`, раздел `Длинный 10-минутный стресс-тест` |

Покрываемые эффекты:

| Эффект | Где проверяется |
| --- | --- |
| Регистрация сенсоров через command requests | `CollectorStressTests.cs` |
| Пакетная отправка values | `CollectorStressTests.cs` |
| HTTP 500 | `CollectorStressTests.cs` |
| Broken connections | `CollectorStressTests.cs` |
| Slow responses | `CollectorStressTests.cs` |
| Managed memory growth в длинном режиме | `CollectorStressTests.cs` |

## Default Sensor Smoke

Назначение: исторические smoke-тесты default sensors.

| Что покрывает | Тестов | Тесты | Где код | Где описание сценария |
| --- | ---: | --- | --- | --- |
| Process CPU default sensor | 1 фактически пустой | `CreateDefaultSensorTest` | `DefaultSensorsTests.cs:24` | Нет; assertions закомментированы |
| Process CPU default sensor with specific path | 1 фактически пустой | `CreateDefaultSensor_WithSpecificPath_Test` | `DefaultSensorsTests.cs:33` | Нет; assertions закомментированы |

Вывод: default/system sensors почти не покрыты реальными assertions.

## Что пока не покрыто или покрыто слабо

| Зона | Текущее покрытие | Что добавить |
| --- | --- | --- |
| Windows default sensors: CPU/RAM/process/service/EventLog | Слабое | Реальные tests с fake/guarded providers или Windows-only integration suite |
| Unix default sensors: CPU/RAM/process | Слабое | Tests на command timeout, parse errors, missing shell tools |
| Системный сенсор недоступен | Слабое | Tests на exceptions от OS API и отсутствие падения collector |
| Permissions denied для OS APIs | Нет | Windows integration tests с ожидаемым graceful failure |
| DNS/proxy/TLS certificate failures | Частично TLS policy уже проверяли кодом, но тестов мало | Transport/TLS integration suite |
| Доставка всех значений на стабильном сервере | Частично | Deterministic delivery test со счетчиком accepted values |
| Large file sensor около production лимитов | Частично: 5 файлов по 64 KB | File stress с большими файлами и memory trend |
| Logger failures | Нет | Custom logger throws/blocks |
| `CollectorOptions` validation | Частично через runtime | Unit tests на invalid options |
| Public API compatibility под `net472` | Сборка есть, API tests мало | Contract tests на основные overloads |
