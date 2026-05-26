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
| 6 | `Permanent_command_sender_failures_do_not_block_dispose` | Постоянные исключения при регистрации сенсоров |
| 7 | `Creating_sensors_after_initialize_under_command_failures_does_not_hang` | Массовое создание сенсоров после старта, когда command sender падает |
| 8 | `Concurrent_add_value_during_dispose_does_not_throw_to_callers` | Одновременные `AddValue()` и `Dispose()` |
| 9 | `Queue_overflow_under_flood_keeps_collector_responsive` | Очень маленькая очередь и сильный поток значений |
| 10 | `Repeated_start_stop_cycles_do_not_leave_sender_active` | Несколько циклов `Start()` / `Stop()` подряд |
| 11 | `Blocked_function_timer_callback_does_not_block_collector_stop` | Зависший пользовательский function callback не должен блокировать `Collector.Stop()` |

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

Результат 10-минутного прогона:

```text
Iterations: 126
Test cases per iteration: 10
Total test case executions: 1260
Failures: 0
Elapsed: 00:10:03.1475314
```

## Как запустить один раз

```powershell
dotnet test .\src\collector\HSMDataCollector.Tests\HSMDataCollector.Tests.csproj --no-restore --filter "FullyQualifiedName~CollectorAdversarialTests"
```

Ожидаемый результат:

```text
Passed: 11
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
- исключения вылетают наружу из параллельных `AddValue()`.

## Связанные документы

Отчет по flaky-server stress-тестам:

`docs/test/CollectorStressTests.md`
