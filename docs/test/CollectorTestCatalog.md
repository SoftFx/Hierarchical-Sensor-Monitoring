# Collector test coverage catalog

Дата: 2026-05-26.

Краткая матрица покрытия тестами `HSMDataCollector`. Цель документа - быстро увидеть, какие группы поведения уже закрыты, сколько тестов есть на тему, где посмотреть код и где прочитать сценарий выполнения.

Процент покрытия - субъективная инженерная оценка, а не code coverage. Она означает, насколько текущие тесты закрывают реальное пространство вариантов поведения: разные тайминги, объемы, ошибки, ресурсы, платформы и повторяемость.

## Summary

| Группа | Быстрых тестов | Оценка покрытия | Длительность | Код | Подробное описание |
| --- | ---: | ---: | --- | --- | --- |
| Transport chaos | 18 fast + 1 gated | 90% | ~35 sec fast suite; gated single-server soak 30 sec default | `src/collector/HSMDataCollector.Tests/CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), [CollectorSuiteSoakTests.md](CollectorSuiteSoakTests.md) |
| Resource leaks | 1 fast + 1 gated repeat | 70% | ~4 sec быстрый; gated suite repeat 30 sec default | `src/collector/HSMDataCollector.Tests/CollectorResourceLeakTests.cs` | [CollectorResourceLeakTests.md](CollectorResourceLeakTests.md), [CollectorSuiteSoakTests.md](CollectorSuiteSoakTests.md) |
| Adversarial lifecycle | 13 fast + 1 gated repeat | 84% | ~2-3 sec быстрый; gated suite repeat 30 sec default | `src/collector/HSMDataCollector.Tests/CollectorAdversarialTests.cs` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), [CollectorBreakCandidates.md](CollectorBreakCandidates.md), [CollectorSuiteSoakTests.md](CollectorSuiteSoakTests.md) |
| Timer stress | 2 fast | 75% | ~3-4 sec fast | `src/collector/HSMDataCollector.Tests/CollectorTimerStressTests.cs` | [CollectorTimerStressTests.md](CollectorTimerStressTests.md) |
| Flaky server stress | 1 fast + 1 gated repeat | 75% | ~3-4 sec быстрый; gated suite repeat 30 sec default; long gated 10 min | `src/collector/HSMDataCollector.Tests/CollectorStressTests.cs` | [CollectorStressTests.md](CollectorStressTests.md), [CollectorSuiteSoakTests.md](CollectorSuiteSoakTests.md) |
| Default sensor smoke | 2 fast + 1 gated repeat | 5% | <1 sec fast; gated suite repeat 30 sec default, но без assertions | `src/collector/HSMDataCollector.Tests/DefaultSensorsTests.cs` | [CollectorSuiteSoakTests.md](CollectorSuiteSoakTests.md); полноценного описания нет, тесты сейчас фактически пустые |

Текущий быстрый прогон:

```text
Passed: 37
Skipped: 7
Failed: 0
Total: 44
Duration: ~45 seconds
```

30-секундный repeat-прогон всех suite: [CollectorSuiteSoakTests.md](CollectorSuiteSoakTests.md). `30 sec` - soft target; hard safety limit по умолчанию `120 sec`.

Каждый gated suite теперь сам снимает ресурсный snapshot до/после suite и пишет объем нагрузки. Для transport/stress/resource suite TCP `ESTABLISHED` и `TIME_WAIT` считаются по тестовым портам. `commands` в отчетах - это command/registration requests коллектора; отдельного login endpoint в тестовом протоколе нет.

Exploratory tests, которые уже сломали collector, и их статус persistent regression перечислены здесь: [CollectorBreakCandidates.md](CollectorBreakCandidates.md).

## Transport Chaos

Назначение: проверить HTTP/TCP транспорт, отмену зависших запросов, socket cleanup, retry behavior и устойчивость к некорректному серверу.

| Что покрывает | Тестов | Покрытие | Длительность | Тесты | Где код | Где описание сценария |
| --- | ---: | ---: | --- | --- | --- | --- |
| Сервер принимает соединение и сразу закрывает | 1 | 75% | ~1 sec | `Server_accepts_and_disconnects_repeatedly_does_not_leak_sockets` | `CollectorTransportChaosTests.cs:32` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `1. Accept and drop` |
| Сервер принимает соединение и никогда не отвечает | 1 | 80% | ~1 sec | `Server_accepts_and_never_responds_dispose_cancels_requests` | `CollectorTransportChaosTests.cs:44` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `2. Accept and never respond` |
| Порт открыт, но серверное приложение вообще не делает accept; 100k mixed values | 1 | 85% | ~3 sec | `Server_socket_is_open_but_never_accepts_while_values_are_added_does_not_hang_or_leak` | `CollectorTransportChaosTests.cs:57` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `3. Open socket, no accept` |
| Сервер принял headers, но не читает body и не отвечает; 100k mixed values | 1 | 85% | ~3 sec | `Server_accepts_but_never_reads_body_or_responds_while_mixed_values_are_generated_stays_bounded` | `CollectorTransportChaosTests.cs:113` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `4. Accept headers, never read body, never respond, high-volume mixed values` |
| Сервер медленно отвечает; 100k mixed values | 1 | 80% | ~3 sec | `Server_accepts_and_replies_slowly_while_mixed_values_are_generated_stays_bounded` | `CollectorTransportChaosTests.cs:168` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `5. Accept and reply slowly, high-volume mixed values` |
| Сервер очень медленно читает request body | 1 | 65% | ~1 sec | `Server_reads_request_body_slowly_does_not_block_dispose` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `6. Slow request-body read` |
| Сервер отправляет headers, но body не заканчивает | 1 | 75% | ~1 sec | `Server_sends_headers_and_never_completes_body_does_not_hang_dispose` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `7. Headers sent, body never completes` |
| Сервер возвращает malformed HTTP | 1 | 70% | ~1 sec | `Server_returns_malformed_http_does_not_leak_connections` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `8. Malformed HTTP` |
| TCP reset во время request body | 1 | 70% | ~1 sec | `Server_resets_connection_during_request_body_does_not_leak_connections` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `9. Reset during request body` |
| `/commands` зависает, data endpoint работает | 1 | 75% | ~1 sec | `Command_endpoint_hangs_data_endpoint_still_disposes` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `10. Command endpoint hangs, data endpoint works` |
| Data endpoint зависает, `/commands` работает | 1 | 75% | ~1 sec | `Data_endpoint_hangs_command_endpoint_still_disposes` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `11. Data endpoint hangs, command endpoint works` |
| Сервер сначала недоступен, потом появляется | 1 | 60% | ~3 sec | `Server_starts_after_connection_refused_collector_recovers` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `12. Server starts after connection refused` |
| Много collectors на один flaky server | 1 | 65% | ~2 sec | `Many_collectors_to_one_flaky_server_do_not_exhaust_resources` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `13. Many collectors to one flaky server` |
| Много collectors на много flaky ports | 1 | 65% | ~2 sec | `Many_collectors_on_many_flaky_ports_do_not_leave_connections` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `14. Many collectors on many flaky ports` |
| Большой string/comment payload под disconnects | 1 | 60% | ~1 sec | `Huge_string_and_comment_payload_under_disconnects_stays_bounded` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `15. Huge string and comment payload` |
| File sensor flood под disconnects | 1 | 55% | ~2 sec | `File_sensor_flood_under_disconnects_releases_files_and_sockets` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `16. File sensor flood` |
| `Dispose()` во время in-flight HTTP request | 1 | 85% | <1 sec | `Dispose_while_http_request_is_mid_flight_closes_connection` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `17. Dispose while HTTP request is mid-flight` |
| Retry storm при постоянном disconnect | 1 | 70% | ~3 sec | `Constant_disconnect_retry_storm_stays_bounded` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `18. Constant disconnect retry storm` |
| Mixed transport suite на одном сервере по кругу | 1 gated | 85% | gated; 30 sec default | `Mixed_transport_chaos_suite_repeated_on_one_server_stays_bounded` | `CollectorTransportChaosTests.cs` | [CollectorTransportChaosTests.md](CollectorTransportChaosTests.md), раздел `Single-server transport soak` |

## Resource Leaks

Назначение: контролировать процессные ресурсы и TCP states при циклах collector/server/load/dispose.

| Что покрывает | Тестов | Покрытие | Длительность | Тесты | Где код | Где описание сценария |
| --- | ---: | ---: | --- | --- | --- | --- |
| Handles, threads, managed memory after GC, private bytes, working set, TCP `ESTABLISHED`, TCP `TIME_WAIT` на коротком flaky HTTP цикле | 1 | 60% | ~4 sec | `Collector_releases_resources_after_flaky_http_cycles` | `CollectorResourceLeakTests.cs:31` | [CollectorResourceLeakTests.md](CollectorResourceLeakTests.md), раздел `Быстрый ресурсный тест` |
| Те же метрики на длинном/gated прогоне с большим количеством циклов | 1 gated | 75% | gated; 10 cycles ~8 sec, default 30 cycles | `Collector_releases_resources_after_long_flaky_http_cycles` | `CollectorResourceLeakTests.cs:48` | [CollectorResourceLeakTests.md](CollectorResourceLeakTests.md), раздел `Длинный ресурсный тест` |

Контролируемые метрики:

| Метрика | Покрытие | Где проверяется |
| --- | ---: | --- |
| `Process.HandleCount` | 65% | `CollectorResourceLeakTests.cs` |
| TCP connections к тестовым портам | 80% | `CollectorResourceLeakTests.cs`, `CollectorTransportChaosTests.cs` |
| TCP `ESTABLISHED` | 85% | `CollectorResourceLeakTests.cs`, `CollectorTransportChaosTests.cs` |
| TCP `TIME_WAIT` | 60% | `CollectorResourceLeakTests.cs` |
| Managed memory after full GC | 65% | `CollectorResourceLeakTests.cs` |
| Private bytes / working set | 60% | `CollectorResourceLeakTests.cs` |
| Thread count | 65% | `CollectorResourceLeakTests.cs` |

## Adversarial Lifecycle

Назначение: точечно ломать lifecycle, очереди, sensor API и поведение sender-а.

| Что покрывает | Тестов | Покрытие | Длительность | Тесты | Где код | Где описание сценария |
| --- | ---: | ---: | --- | --- | --- | --- |
| CPU spin в `RateSensor` после `double.NaN` | 1 | 90% | <1 sec | `Rate_sensor_nan_value_does_not_spin_forever` | `CollectorAdversarialTests.cs:18` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), таблица `Какие сценарии проверяются` |
| `Stop()` после legacy `Initialize(false)` | 1 | 85% | <1 sec | `Stop_after_initialize_stops_data_delivery` | `CollectorAdversarialTests.cs:34` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), таблица `Какие сценарии проверяются` |
| Race condition: `Stop()` во время pending `Start(...)` | 1 | 70% | <1 sec | `Stop_while_start_is_pending_does_not_leave_collector_running` | `CollectorAdversarialTests.cs:59` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), таблица `Какие сценарии проверяются` |
| `Dispose()` отменяет заблокированный data sender | 1 | 80% | <1 sec | `Dispose_cancels_blocked_data_sender` | `CollectorAdversarialTests.cs:78` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), таблица `Какие сценарии проверяются` |
| Постоянные ошибки data sender | 1 | 75% | <1 sec | `Permanent_data_sender_failures_do_not_block_dispose` | `CollectorAdversarialTests.cs:99` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), таблица `Какие сценарии проверяются` |
| Постоянные ошибки command sender | 1 | 75% | <1 sec | `Permanent_command_sender_failures_do_not_block_dispose` | `CollectorAdversarialTests.cs:122` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), таблица `Какие сценарии проверяются` |
| Создание сенсоров после `Initialize()` при command failures | 1 | 70% | <1 sec | `Creating_sensors_after_initialize_under_command_failures_does_not_hang` | `CollectorAdversarialTests.cs:143` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), таблица `Какие сценарии проверяются` |
| Параллельный `AddValue()` во время `Dispose()` | 1 | 70% | <1 sec | `Concurrent_add_value_during_dispose_does_not_throw_to_callers` | `CollectorAdversarialTests.cs:164` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), таблица `Какие сценарии проверяются` |
| Overflow маленькой очереди | 1 | 70% | <1 sec | `Queue_overflow_under_flood_keeps_collector_responsive` | `CollectorAdversarialTests.cs:201` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), таблица `Какие сценарии проверяются` |
| Повторные `Start()` / `Stop()` циклы | 1 | 75% | <1 sec | `Repeated_start_stop_cycles_do_not_leave_sender_active` | `CollectorAdversarialTests.cs:222` | [CollectorAdversarialTests.md](CollectorAdversarialTests.md), таблица `Какие сценарии проверяются` |

## Timer Stress

Назначение: проверить общий timer/scheduler слой, разные `PostDataPeriod`, restart timer-а и CPU-budget.

| Что покрывает | Тестов | Покрытие | Длительность | Тесты | Где код | Где описание сценария |
| --- | ---: | ---: | --- | --- | --- | --- |
| Function sensors с разными периодами `40-500 ms` | 1 | 75% | ~2 sec | `Function_sensors_with_varied_timer_periods_fire_without_cpu_spin` | `CollectorTimerStressTests.cs` | [CollectorTimerStressTests.md](CollectorTimerStressTests.md), раздел `Что проверяется` |
| Restart function timer с `100 ms` на `25 ms`, callback длится `40 ms` | 1 | 80% | ~1 sec | `Restarting_function_timer_under_load_changes_rate_without_callback_overlap` | `CollectorTimerStressTests.cs` | [CollectorTimerStressTests.md](CollectorTimerStressTests.md), раздел `Что проверяется` |

## Flaky Server Stress

Назначение: нагрузочная отправка sensor values через HSM-like HTTP server с `200`, `500`, broken connections и slow responses.

| Что покрывает | Тестов | Покрытие | Длительность | Тесты | Где код | Где описание сценария |
| --- | ---: | ---: | --- | --- | --- | --- |
| Быстрый flaky HTTP stress под параллельной нагрузкой | 1 | 70% | ~3-4 sec | `Collector_survives_transient_server_failures_under_parallel_load` | `CollectorStressTests.cs:30` | [CollectorStressTests.md](CollectorStressTests.md), раздел `Быстрый стресс-тест` |
| 10-minute sustained flaky HTTP stress | 1 gated | 85% | gated; 10 min | `Collector_runs_for_ten_minutes_against_flaky_server_under_sustained_load` | `CollectorStressTests.cs:68` | [CollectorStressTests.md](CollectorStressTests.md), раздел `Длинный 10-минутный стресс-тест` |

Покрываемые эффекты:

| Эффект | Покрытие | Где проверяется |
| --- | ---: | --- |
| Регистрация сенсоров через command requests | 75% | `CollectorStressTests.cs` |
| Пакетная отправка values | 75% | `CollectorStressTests.cs` |
| HTTP 500 | 80% | `CollectorStressTests.cs` |
| Broken connections | 80% | `CollectorStressTests.cs` |
| Slow responses | 75% | `CollectorStressTests.cs` |
| Managed memory growth в длинном режиме | 60% | `CollectorStressTests.cs` |

## Default Sensor Smoke

Назначение: исторические smoke-тесты default sensors.

| Что покрывает | Тестов | Покрытие | Длительность | Тесты | Где код | Где описание сценария |
| --- | ---: | ---: | --- | --- | --- | --- |
| Process CPU default sensor | 1 фактически пустой | 0% | <1 sec | `CreateDefaultSensorTest` | `DefaultSensorsTests.cs:24` | Нет; assertions закомментированы |
| Process CPU default sensor with specific path | 1 фактически пустой | 0% | <1 sec | `CreateDefaultSensor_WithSpecificPath_Test` | `DefaultSensorsTests.cs:33` | Нет; assertions закомментированы |

Вывод: default/system sensors почти не покрыты реальными assertions.

## Что пока не покрыто или покрыто слабо

| Зона | Текущее покрытие | Оценка | Что добавить |
| --- | --- | ---: | --- |
| Windows default sensors: CPU/RAM/process/service/EventLog | Слабое | 5% | Реальные tests с fake/guarded providers или Windows-only integration suite |
| Unix default sensors: CPU/RAM/process | Слабое | 10% | Tests на command timeout, parse errors, missing shell tools |
| Системный сенсор недоступен | Слабое | 10% | Tests на exceptions от OS API и отсутствие падения collector |
| Permissions denied для OS APIs | Нет | 0% | Windows integration tests с ожидаемым graceful failure |
| DNS/proxy/TLS certificate failures | Частично TLS policy уже проверяли кодом, но тестов мало | 25% | Transport/TLS integration suite |
| Доставка всех значений на стабильном сервере | Частично | 35% | Deterministic delivery test со счетчиком accepted values |
| Large file sensor около production лимитов | Частично: 5 файлов по 64 KB | 35% | File stress с большими файлами и memory trend |
| Logger failures | Нет | 0% | Custom logger throws/blocks |
| `CollectorOptions` validation | Частично через runtime | 35% | Unit tests на invalid options |
| Public API compatibility под `net472` | Сборка есть, API tests мало | 30% | Contract tests на основные overloads |
