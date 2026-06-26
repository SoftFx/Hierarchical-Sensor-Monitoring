# Collector adversarial tests

Дата прогона: 2026-05-25.

Документ описывает adversarial-тесты для `HSMDataCollector`: это не happy-path нагрузка, а набор сценариев, специально подобранных так, чтобы попытаться сломать коллектор, подвесить его, заставить жечь CPU, потерять lifecycle-состояние или перестать корректно останавливать отправку.

## Где находится код

Файл тестов:

`src/collector/HSMDataCollector.Tests/CollectorAdversarialTests.cs`

Тестовый проект:

`src/collector/HSMDataCollector.Tests/HSMDataCollector.Tests.csproj`

## Какие сценарии проверяются

| N | Тест | Что пытается сломать |
| --- | --- | --- |
| 1 | `Rate_sensor_nan_value_does_not_spin_forever` | CPU-spin в `RateSensor` после попадания `double.NaN` во внутреннюю сумму |
| 2 | `Stop_after_initialize_stops_data_delivery` | Legacy `Initialize(false)` + `Stop()`: проверяет, что после `Stop()` отправка реально прекращается |
| 3 | `Stop_while_start_is_pending_does_not_leave_collector_running` | Race condition между `Start(customTask)` и `Stop()` |
| 4 | `Dispose_cancels_blocked_data_sender` | Зависший data sender, который отпускается только отменой токена |
| 5 | `Permanent_data_sender_failures_do_not_block_dispose` | Постоянные исключения при отправке data-пакетов |
| 6 | `Stop_with_data_sender_that_ignores_cancellation_does_not_hang` | Пользовательский `IDataSender` зависает и игнорирует `CancellationToken`; `Stop()` должен иметь верхний предел ожидания |
| 7 | `Stop_with_priority_sender_that_ignores_cancellation_does_not_hang` | Priority queue sender зависает и игнорирует cancellation; `Stop()` не должен зависнуть |
| 8 | `Stop_with_command_sender_that_ignores_cancellation_does_not_hang` | Command queue sender зависает и игнорирует cancellation; `Stop()` не должен зависнуть |
| 9 | `Stop_with_file_sender_that_ignores_cancellation_does_not_hang` | File queue sender зависает и игнорирует cancellation; `Stop()` не должен зависнуть |
| 10 | `Stop_with_uncancellable_data_sender_and_backlog_does_not_hang` | Пользовательский `IDataSender` зависает на первом пакете, а в очереди остается backlog; stop-flush не должен зависнуть вторым send path |
| 11 | `Permanent_command_sender_failures_do_not_block_dispose` | Постоянные исключения при регистрации сенсоров |
| 12 | `Message_deduplicator_handles_concurrent_duplicate_bursts` | Concurrent bursts одинаковых exception messages не должны блокироваться на глобальном lock-е |
| 13 | `Creating_sensors_after_initialize_under_command_failures_does_not_hang` | Массовое создание сенсоров после старта, когда command sender падает |
| 14 | `Concurrent_add_value_during_dispose_does_not_throw_to_callers` | Одновременные `AddValue()` и `Dispose()` |
| 15 | `Creating_sensor_while_stop_is_in_progress_does_not_start_it` | Сенсор, созданный во время `Stop()`, не должен стартовать fire-and-forget init/send path |
| 16 | `Concurrent_sensor_registration_for_same_path_returns_existing_sensor` | Параллельная регистрация одного path должна вернуть один общий sensor instance |
| 17 | `Queue_overflow_under_flood_keeps_collector_responsive` | Очень маленькая очередь и сильный поток значений |
| 18 | `Repeated_start_stop_cycles_do_not_leave_sender_active` | Несколько циклов `Start()` / `Stop()` подряд |
| 19 | `Values_added_while_stopped_are_not_sent_after_restart` | Значения, добавленные после `Stop()`, не должны копиться в очередях и уезжать после следующего `Start()` |
| 20 | `Last_value_sensor_flushes_latest_value_on_stop` | `LastValueSensor` должен отправить последнее значение при `Stop()` без ожидания полного `PackageCollectPeriod` |
| 21 | `Last_value_sensor_flushes_latest_value_on_dispose` | `Dispose()` активного collector-а должен выполнить bounded shutdown/flush и не потерять последнее значение |
| 22 | `Stop_flush_does_not_resend_same_value_when_sender_does_not_enumerate_items` | Queue flush не должен зависеть от того, перечисляет ли пользовательский `IDataSender` переданный `IEnumerable` |
| 23 | `Stop_during_slow_data_sends_completes_without_parallel_flush` | `Stop()` не должен запускать flush параллельно обычному data processing loop, когда sender медленный |
| 24 | `Concurrent_start_calls_initialize_collector_once` | Массовые параллельные `Start()` не должны повторно инициализировать sensor commands |
| 25 | `Sensor_count_limit_rejects_excess_bar_sensors_before_start` | Злоупотребление cardinality: слишком много bar sensors до старта должно падать быстро, а не идти к OOM |
| 26 | `Sensor_count_limit_is_enforced_after_collector_start` | Тот же лимит после `Start()`, когда новые сенсоры сразу инициализируются и стартуют |
| 27 | `Blocked_function_timer_callback_does_not_block_collector_stop` | Зависший пользовательский function callback не должен блокировать `Collector.Stop()` |
| 28 | `Stop_waits_for_short_function_timer_callback_before_disposing_sensor` | Короткий in-flight callback должен успеть завершиться и попасть в stop flush |
| 29 | `Start_after_stop_timeout_does_not_mark_collector_running_until_queues_finish` | Restart после timeout остановки worker-а не должен переводить collector в `Running` раньше реального завершения queues |
| 30 | `Explicit_http_server_address_requires_plaintext_opt_in` | Явный `http://` endpoint апгрейдится до HTTPS, если plaintext transport не разрешен явно |
| 31 | `Lifecycle_event_handler_exception_does_not_escape_collector_stop` | Исключение из пользовательского lifecycle event handler не должно вылетать наружу из `Collector.Stop()` |
| 32 | `Data_sender_dispose_exception_does_not_escape_collector_dispose` | Исключение из пользовательского `IDataSender.Dispose()` не должно вылетать наружу из `DataCollector.Dispose()` |
| 33 | `Start_after_dispose_does_not_resurrect_collector` | `Start()` после `Dispose()` не должен оживлять collector поверх закрытых ресурсов |
| 34 | `Initialize_after_dispose_does_not_resurrect_collector` | Legacy `Initialize(false)` после `Dispose()` не должен оживлять collector поверх закрытых ресурсов |
| 35 | `Throwing_ExceptionThrowing_subscriber_does_not_stop_sensor_loop` | Host-callback matrix (#1103): бросающий `ExceptionThrowing` subscriber не должен останавливать send loop сенсора (до фикса #1102-A1 он убивал процесс через async-void dispatch) |
| 36 | `Throwing_ExceptionThrowing_subscriber_does_not_starve_other_subscribers` | Host-callback matrix (#1103): изоляция per-subscriber — бросающий подписчик не блокирует следующих |
| 37 | `Throwing_onError_callback_does_not_kill_scheduler_or_other_tasks` | Host-callback matrix (#1103): бросающий `onError` на seam шедулера не должен убивать worker или морить здоровые задачи |
| 38 | `Throwing_custom_logger_does_not_escape_collector_operations` | Host-callback matrix (#1103): бросающий `ICollectorLogger` изолируется на всех операциях коллектора |
| 39 | `Throwing_lifecycle_listener_does_not_affect_transitions_or_other_listeners` | Host-callback matrix (#1103): бросающий `ILifecycleListener` не ломает transitions и не блокирует других listeners |

Процессная половина host-callback matrix (краш хоста нельзя заассертить in-process) живет в `CollectorCrashIsolationTests` + `HSMDataCollector.CrashTests.Host`: см. [CollectorTestCatalog.md](CollectorTestCatalog.md), раздел `Crash Isolation`.

## Что эти тесты уже нашли

Первый запуск adversarial-набора нашел 3 дефекта:

| Дефект | Симптом | Исправление |
| --- | --- | --- |
| `RateSensor` зависал после `double.NaN` | следующий `AddValue(...)` не завершался за 1 секунду | `52da7726b Fix rate sensor NaN accumulation spin` |
| `Stop()` после `Initialize(false)` не останавливал отправку | после `Stop()` счетчик data packages продолжал расти | `4c2375a5e Fix collector lifecycle stop states` |
| `Stop()` во время pending `Start(...)` оставлял статус `Starting` | ожидался `Stopped`, фактически был `Starting` | `4c2375a5e Fix collector lifecycle stop states` |

После исправлений весь adversarial-набор стал зеленым.

Позже отдельный exploratory test нашел еще один lifecycle-дефект:

| Дефект | Симптом | Исправление |
| --- | --- | --- |
| `Stop()` ждал зависший function timer callback | `Collector.Stop()` не завершался за 2 секунды, пока callback не был отпущен | `5b5856873 Prevent blocked timer callbacks from hanging stop` |
| Exception из lifecycle event handler вылетал наружу из `Stop()` | пользовательский `ToStopped` handler мог кинуть exception, который возвращался вызывающему приложению | `Isolate lifecycle event handler failures` |
| Exception из пользовательского `IDataSender.Dispose()` вылетал наружу из `DataCollector.Dispose()` | приложение могло получить exception во время штатного cleanup collector-а | `Isolate data sender dispose failures` |
| `Start()` / `Initialize(false)` после `Dispose()` оживляли collector | статус становился `Running` после того, как ресурсы уже были освобождены | `Start()` и legacy `Initialize()` теперь no-op после dispose |
| Значения, добавленные после `Stop()`, отправлялись после следующего `Start()` | stopped collector копил payload в очередях; после restart уходили stale values и хвосты diagnostic package values | enqueue закрыт, когда collector не started; очереди очищаются на `Stop()` |
| `LastValueSensor` не отправлял последнее значение на `Stop()` | `Stop()` завершался без data package, хотя `LastValueSensor.StopAsync()` вызывает `SendValue()` | сенсоры останавливаются до остановки очередей; data/priority queues делают bounded flush |
| `LastValueSensor` не отправлял последнее значение на `Dispose()` | активный collector при `Dispose()` освобождал очереди раньше остановки сенсоров, последнее значение терялось | `Dispose()` активного collector-а сначала выполняет bounded `DataProcessor.StopAsync()`, потом освобождает ресурсы |
| Queue flush зависел от ленивого `IEnumerable` | один last-value при `Stop()` вызвал 3 852 549 повторных `SendDataAsync`, если `IDataSender` не перечисляет items | очередь материализует package items до вызова sender-а, поэтому dequeue и package stats не зависят от внешнего sender-а |
| `Stop()` мог пересекать bounded flush с обычным processing loop | медленный sender позволял двум data send paths работать одновременно во время остановки | data/priority processing loops сначала останавливаются без очистки очереди, затем выполняется bounded flush и cleanup |
| Priority queue могла уйти в tight retry loop | failed priority send возвращал package в channel и немедленно перечитывал его без backoff | после failed priority send добавлен bounded delay перед retry |
| Параллельные `Start()` не были атомарны | несколько callers могли пройти проверку `Stopped` до перехода в `Starting` | lifecycle transition защищен lock-ом, а `DataProcessor.Start()` стал идемпотентным |
| `Stop()` мог зависнуть на sender-е, который игнорирует cancellation | пользовательский `IDataSender` вошел в `SendDataAsync` и не реагировал на отмену; `Collector.Stop()` не завершался за 2 секунды | ожидание остановки queue worker-а ограничено `RequestTimeout`; новый worker не стартует поверх старого зависшего |
| Stop-flush мог снова зависнуть после timeout остановки worker-а | первый send завис и игнорировал cancellation, а backlog остался в очереди; после timeout `Stop()` входил в flush и зависал на втором send path | queue stop возвращает признак реальной остановки worker-а; flush выполняется только если worker действительно остановлен |
| Rollback failed restart мог очистить сохраненную очередь | часть queues уже стартовала, поздняя queue отказалась стартовать, rollback вызывал destructive `StopAsync()` | rollback останавливает уже стартованные queues с `clearQueue: false` |
| Количество сенсоров не было ограничено | пользователь мог создать очень много уникальных sensor path-ов; для bar/function/rate sensors это ведет к росту памяти, command queue и scheduled tasks | добавлен `CollectorOptions.MaxSensors`; превышение лимита бросает `InvalidOperationException` до старта и после старта |

## Длинный локальный прогон

Команда запуска:

```powershell
$deadline = (Get-Date).AddMinutes(10)
$iteration = 0
$failures = 0
$start = Get-Date
while ((Get-Date) -lt $deadline) {
    $iteration++
    dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorAdversarialTests" --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        $failures++
        break
    }
}
$elapsed = (Get-Date) - $start
```

Результат 10-минутного прогона до добавления lazy-enumeration regression:

```text
Iterations: 126
Test cases per iteration: 17
Total test case executions: 2142
Failures: 0
Elapsed: 00:10:03.1475314
```

## Как запустить один раз

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorAdversarialTests"
```

Ожидаемый результат:

```text
Passed: 35
Failed: 0
Skipped: 1
```

## Как запустить на 10 минут

```powershell
$deadline = (Get-Date).AddMinutes(10)
$iteration = 0
$failures = 0
while ((Get-Date) -lt $deadline) {
    $iteration++
    Write-Host "==== Adversarial iteration $iteration at $((Get-Date).ToString('HH:mm:ss')) ===="
    dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorAdversarialTests" --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        $failures++
        break
    }
}
Write-Host "Iterations: $iteration"
Write-Host "Failures: $failures"
```

## Что считать плохим результатом

Любой из этих признаков считается проблемой:

- тест зависает и не доходит до итоговой строки `Passed`;
- появляется `Failed > 0`;
- `Dispose()` не завершается в заданный timeout;
- после `Stop()` продолжается отправка новых пакетов;
- `RateSensor.AddValue(...)` не возвращает управление после `NaN`;
- статус коллектора остается `Starting` или `Running` после остановки;
- значение, добавленное после `Stop()`, отправляется после следующего `Start()`;
- `LastValueSensor` не отправляет последнее значение при `Stop()`;
- `LastValueSensor` не отправляет последнее значение при `Dispose()` активного collector-а;
- один queued value вызывает повторные отправки одного и того же package из-за ленивого sender enumeration;
- `Stop()` запускает flush параллельно обычной отправке data queue;
- `Stop()` зависает на пользовательском `IDataSender`, который игнорирует `CancellationToken`;
- `Stop()` зависает на priority, command или file queue sender-е, который игнорирует `CancellationToken`;
- `Stop()` после timeout остановки worker-а снова зависает на flush backlog-а;
- retry priority queue при постоянной ошибке жжет CPU без backoff;
- sensor, созданный во время `Stop()`, стартует и отправляет command/data поверх shutdown;
- слишком много уникальных сенсоров создается без явного upper bound;
- параллельные `Start()` повторно инициализируют collector или command packages;
- `Start()` или `Initialize()` переводят disposed collector обратно в `Running`;
- исключения вылетают наружу из параллельных `AddValue()`.

## Связанные документы

Отчет по flaky-server stress-тестам:

`docs/test/CollectorStressTests.md`
